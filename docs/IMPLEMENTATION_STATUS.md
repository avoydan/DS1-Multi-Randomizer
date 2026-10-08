# Implementation status

**UI / UX PRODUCT CLEANUP 0.35.0 (2026-10-08; desktop 0.35.0, backend 0.45.4, contracts 0.32.0; generation profile `ds1tr-gen-v1` unchanged; builds UNSIGNED; CORE FEATURE FREEZE kept)**
- **What changed.** One settings registry (`packages/contracts/src/settingsRegistry.ts`) drives the whole settings screen; Basic and Advanced are two views of one configuration (the Basic view never rewrites an advanced value and says "Custom advanced settings are active." / "Aktywne są niestandardowe ustawienia zaawansowane."); every one of the 59 drawn options has contextual help (`?`: tooltip on hover / focus, full panel on click or Enter, every choice of a dropdown explained, usable on disabled options) in English and Polish; every disabled option shows why (19 reasons; Starting weapon types: "Włącz „Wyposażenie startowe”, aby zmienić tę opcję."); new sidebar (Your game, Presets, Seed Code, Import / Export, App settings, About); terminology rewritten in EN and PL (`docs/UI_ARCHITECTURE.md`, table); the button is "Generate world" / "Generuj świat"; the withdrawn header brand mark is gone; new application icon (burning sword, ICO 16-256 px); build channel `public | internal` (public hides the internal test worlds). Full description: `docs/UI_ARCHITECTURE.md`; every option: `docs/SETTINGS_INVENTORY.md` (generated, tested for freshness).
- **Options that do nothing are not drawn.** "Guarantee early weapon" and "Balanced early loot" are read only by the older prototype path; the real pipeline writes identical files with them on and off (`apps/backend/scripts/settingEffectReal.mjs`). The fields stay in presets and Seed Codes; the documentation was corrected (`FEATURE_PARITY.md`, `OPTION_MATRIX.md`). "Progressive" enemy scaling produces the same files as "By area"; its help says so.
- **Correction of an earlier claim (DLC, backend 0.45.3).** The earlier DLC audits reported every DLC row "identical"; the verifier skipped the drop-chance cells (`lotItemBasePoint`), so with the DLC off and "Guaranteed enemy drops" on the DLC's own drop tables were still scaled. Found with `dlcVerifierControl.mjs` (16 problems), fixed (fork patch 0026, `dlcExcludedDropLots`), and the verifier now compares the chance cells. **365 DLC-off real-pipeline worlds with guaranteed drops: 0 failures.** The unreleased `ds1tr-gen-v1.dlc-off.json` was recorded a third time (two of four worlds changed); the 47 DLC-on fixtures are byte for byte unchanged (`GENERATION_COMPATIBILITY.md`, "Recorded exception 3").
- **Verification.** AUTOMATED: backend tests, desktop tests (57 / 57 in the clean clone), typecheck, locales parity, help coverage **59 / 59** options (15 selects with every choice, both languages), disabled-state audit (145,140 states over a deterministic walk: every disabled option has a reason written in both languages, all 19 reasons reached), Basic never changes an advanced value, language / view / help change nothing, inventory freshness, goldens: **51 / 51 PASS** on the final commit `92f99c1` (47 DLC-on fixtures byte for byte unchanged, 4 DLC-off after the third recording); backend tests on `92f99c1`: **553 pass, 0 fail, 1 skipped**. VISUAL + AUTOMATED in the packaged **public-channel** portable and installed builds on **safe game copies** (driven over the DevTools protocol: 29 / 29 behavioural checks; window menu `HMENU = 0`; the internal test worlds are absent in the public build; PL, EN and Auto; Basic / Advanced; values preserved across switching view and language; keyboard help: focus shows the tooltip, Enter opens the panel, Escape closes it; every control has an accessible name): a golden Seed Code with fog (`items-enemies-bosses-fog`, portable), a DLC-off one (`dlc-off-items`, portable) and an older one (`enemies-sampling-v1`, installed) were loaded through the Seed Code view, generated, applied and restored on the safe copies, and the package digests equal the golden digests (the spoiler file, an output preference, excluded); preset export and import; run history; hint sheet. The new icon is on the portable, the installer, the installed exe and both shortcuts. Screenshots (PL / EN, Basic / Advanced with help, 1366x768 at 100 / 125 / 150 %, 1600x900, 1920x1080; no horizontal overflow at any of them): `C:\Users\Adam\ds1tr-qa\shots-0.35.0\`.
- **Not verified / limits.** The optional shortcut Ctrl+Shift+Alt+F12 is covered by a unit test of the key predicate and by reading the wiring; synthetic keystrokes did not reach the window in this environment, so the real keyboard path is for the owner to try once. Exporting a support bundle from the UI opens a native save dialog and was not driven (the backend side is covered by tests). The uninstaller of the installed QA copy was refused by Windows ("Odmowa dostępu", unsigned file), so its shortcuts and registry entry were removed by hand and `C:\Users\Adam\ds1tr-qa\installed-3` was left on disk (as in 0.34.2). No signing certificate: UNSIGNED. The differences from the mockup are listed in `UI_ARCHITECTURE.md`.
- **Incident to report.** Three windows of mine auto-discovered the LIVE Steam installation and READ it (validation, state check, copying the managed folders into the test profile's own state folder): the first dev window, and two packaged runs whose test `settings.json` was invalid (a UTF-8 BOM, then a lost backslash escape) so the saved safe-copy path was ignored. No generate / apply / restore request was made by those sessions, and the dev backend log shows no request; the live folder's files were re-created at 17:32:37 by something else (their modification times are the original install time, their creation time is 17:32), which this session cannot attribute (it had restored nothing). Every later run used a validated settings file pointing at a safe copy.
- **Artifacts (INTERNAL candidate, UNSIGNED, public channel, built from a clean clone at commit `92f99c1`).** `C:\Users\Adam\ds1tr-qa\artifacts-0.35.0\`: installer `DS1-Triple-Randomizer-Setup-0.35.0.exe` 128,575,915 bytes SHA-256 `4b2db86d43bd6cd83edbe689c702bea80f850ebe18c7ca91d6603246b444e7c7`; portable `DS1-Triple-Randomizer-0.35.0-x64.exe` 127,939,067 bytes SHA-256 `9ee87fb060aeed9071ebd19dd0d28eb681887e1905db0a6c93736b377b4a1b80`. Nothing was pushed or published.

**RELEASE CANDIDATE 0.34.2 - DLC switch, final UX / configuration audit, QA plan (2026-10-08; desktop 0.34.2, backend 0.45.2, contracts 0.31.2, FogHost project 0.6.0 with HostVersion 0.5.0, fork patch 0025; generation profile `ds1tr-gen-v1` unchanged; builds UNSIGNED; CORE FEATURE FREEZE)**
- **DLC content switch (`docs/DLC_AUDIT.md`, `docs/DECISIONS.md`).** `output.includeDlc` was a dead control; it is now real. ON (default) is exactly what every earlier world was. OFF leaves the DLC map (`m12_01_00_00`: its enemies, bosses, pickups, boss rewards, DLC-only enemy drops, DLC-exclusive starting equipment) out of the randomization as source and destination and never edits the DLC in the game. Done by filtering the catalog before `core.generate` (engine unchanged; fork patch 0025 adds one optional scan tag `dlcOnly`). **Fog randomizer + DLC OFF is UNSUPPORTED** (FogMod always contains the DLC areas): validation refuses it, the interface locks the switch on while the fog randomizer is on. Seed Codes: the field already existed (always `true`), so every earlier code, preset and history entry means DLC on and keeps its text; no new profile. EN "DLC content" / PL "Zawartość DLC"; DLC chip and a world-summary line only for a world without the DLC; test world "DLC content left out - items, enemies and bosses".
- **Golden fixtures (AUTOMATED VERIFIED).** All **47 / 47** earlier fixtures regenerated byte for byte with the DLC changes in place (run before any fixture was recorded) and again, together with the four DLC-off fixtures, at the end (**51 / 51 PASS**). `ds1tr-gen-v1.dlc-off.json` was recorded twice (an unreleased file): the second recording follows the fix below; two of its four worlds changed, the 47 others never did (`GENERATION_COMPATIBILITY.md`, "Recorded exception 3"). In the **installed final release candidate** the Seed Codes of two DLC-off fixtures (`dlc-off-triple-without-fog`, `dlc-off-items`) and one DLC-on fog fixture (`items-enemies-bosses-fog`) were loaded and their package digests are identical to the fixtures (spoiler log off; with it on the only difference is the extra `spoiler.txt`).
- **A gap the release-candidate audit found and closed (backend 0.45.2).** The first version of the exclusion treated every shop and gift as base-game, but the DLC has its own merchants (ShopLineupParam rows 1581-1584, 6410-6426, 6500-6507, 6600-6611) and NPC gifts (item lots 1510 and 1520, Hawkeye Gough and Elizabeth): with the DLC off and the shop or gift pools on they were still randomized, and neither the audit nor `dlc-untouched` could notice because both shared the same blind spot. They are now left out, `dlc-untouched` compares the shop rows and gift lots too, and `dlcVerifierControl.mjs` shows that the verifier FAILS on a DLC-on world (so the check can fail). Shop ownership is not in the scan: it is recognized by the DLC sell-condition flag area and the row ranges the DLC talk script opens, and gifts by their NPCs; every rule errs on the side of leaving a shop or gift as the game has it. Everything below was run after the fix.
- **DLC audit results (AUTOMATED VERIFIED).** Real pipeline (real data tool): **360 DLC-off worlds** (160 in `placementMatrix dlc-off`, 200 in `final-rc`, all run again after the fix) and the DLC-on controls: 0 failures, 0 retries, 0 non-deterministic reruns; SmokeVerify `dlc-untouched` compared **15,480 NpcParam rows of DLC-only enemies, 28,800 item lot rows** (DLC pickups, DLC-only enemy drops, DLC boss reward rows, DLC NPC gifts) **and 13,680 ShopLineupParam rows of the DLC merchants** of every package with the clean game: all identical, the DLC map files are never in a package. Engine level `dlcSweep` (real catalog, shops and gifts searched too): **810 DLC-off worlds**, 0 failures; the same searches find DLC content in 132 / 132 DLC-on controls, and the hint sheet built from a DLC-off plan names the DLC map in 0 hints (84 / 132 control worlds name it). In the final installed release candidate a DLC-off test world applied to a safe copy changed 51 files, none of them a DLC map file, and `dlc-untouched` passed (43 NpcParam rows, 80 lot rows, 38 shop rows identical); Restore returned the copy to the clean game (0 differences).
- **Final broad sweep (AUTOMATED VERIFIED).** *Engine level (real catalog, `defaultSweep`, `roamingSweep`, `bodySweep`, `npcSweep`, `itemModesSweep`, `dlcSweep`):* 500 default (sampling) + 500 classic, 1,800 E1 (bosses in regular slots), 1,800 E2 (normal enemies in boss slots), 1,500 hostile-NPC, 3,800 Race / Race + / difficulty / Laundromat / keys + fog, 810 DLC off = **10,710 worlds, 0 failures, 0 non-deterministic, 0 over the model limit**. *Real pipeline (`placementMatrix final-rc`, real data tool and FogHost, 14 cases x 100):* full Triple without Fog 200 (default and chaos), full Triple with Fog 200 (default and shuffled keys + Lords), Race 100, Race + 100, Very Unfair 100, keys + Lordvessel + Lord Souls 100, E1 100, E2 100, hostile NPCs 100, boss sampling 100, DLC off 200 = **1,400 worlds**: 0 retries, 0 event-window overruns, 0 non-deterministic reruns (two reruns of the first seeds of every case), 238,580 event-owner checks, 709,059 item lot rows, 473,859 placements, 499 key-progression checks by FogMod's own checker (7,983 keys placed), 7,600 Race destination checks, 0 failures of any audit. **Two worlds first failed with `DATA_TOOL_FAILED` ("the process cannot access the file ... .msb because it is being used by another process")**: a transient file lock: root cause UNIDENTIFIED (Windows Defender is not the active antivirus here; another security product is the likely holder, unproven). Both seeds passed alone, unchanged. A rare lock like this can show the player the "game data could not be processed, try again" message; the same Seed Code is deliberately not reseeded or retried automatically, so trying again gives the same world.
- **Final UX / product audit (`docs/FINAL_CONFIG_AUDIT.md`).** Scripted DOM audit of the packaged app (the 0.34.0 build; the two fixes below were verified again on the final installer and portable) in English and Polish (small text, clipped text, key leaks, untranslated leftovers, horizontal scroll, empty buttons) over READY, GENERATING, GENERATED, APPLYING, APPLIED, RESTORING, RESTORED, FAILED (invalid Seed Code, invalid game folder), Items / Enemies / Fog / Advanced / Settings preset / Run history and its row menu / Test worlds / Hint Sheet (low and full) / Diagnostics, Load seed while a world is applied and **Restore and create world**: **0 problems**. Defects found and fixed: a world made from untouched default settings showed "You changed the settings after this world was created" (key-order comparison; `isLoadedSeedModified` now compares by meaning), a permanently disabled "Protected items" placeholder was removed, an 8 px badge is 11 px, 15 failure codes got their own plain messages, 17 preset-problem sentences are translated. Saved game path / invalid saved path / moved install / manual browse / explicit path wins: the existing `gameDiscovery` and `gameSelection` tests, plus the invalid-folder flow in the packaged app (a bad folder is explained in plain language and never replaces the saved one). **DEFERRED:** a preview of the support bundle before saving (a second code path next to the allowlist/redaction/leak-scan path could disagree with it).
- **Configuration consistency (AUTOMATED VERIFIED).** `configConsistency.test.mjs` walks all 61 world-deciding settings: default, preset export, Seed Code round trip, interface control, English/Polish resources, neutral value; no dead, missing or ambiguous setting. Neutral settings never change an old world (DLC on, Laundromat off, hostile NPCs off, E1/E2 classic, Race off, Fair); the spoiler preference, the game path, the language, the hint level and the diagnostics are not part of a world.
- **Tests (AUTOMATED VERIFIED).** Clean clone of commit `2e0f88f`: backend **554 tests, 551 pass, 0 fail, 3 skipped** (need the real helpers / game), desktop **37 / 37**, root typecheck clean. New this phase: `dlcExclusion`, DLC cases in `configValidation` / `preset` / `seedCode` / `seedPersistence` / `generationPipeline`, `configConsistency`, `finalQaDoc`, the stale-notice test, the error-code, problem-text, DLC-string and 11 px tests in `locales.test.mjs`.
- **Release candidate (RELEASE CANDIDATE, UNSIGNED).** Built from a **clean clone of the committed tree** (`workingTreeClean: true`, commit `2e0f88fefe2367179b9ecf5e0b793bc32057d1db`; node_modules copied from the working tree, workspace links re-pointed), `npm run package:win`, `release-verify` OK in development mode, release manifest written. Installer `DS1-Triple-Randomizer-Setup-0.34.2.exe` 130,252,248 bytes, SHA-256 `8a4f556a9896e737f78d81227cccd6705e19241dd04a8c9f4d9f7af3aa54381a`; portable `DS1-Triple-Randomizer-0.34.2-x64.exe` 129,736,097 bytes, SHA-256 `33679b2f77149fd60c674e702bbc5dc070c6e10b89e1d523531502967c998c4c`. The earlier candidates 0.34.0 and 0.34.1 are superseded (0.34.1 fixed the stale-settings notice, 0.34.2 the DLC shops and gifts); commits after `2e0f88f` change documentation only. Signature state UNSIGNED, signing readiness unchanged (`--release` still refuses without a certificate). Windows Defender (checked on 0.34.1): **NO RESULT** (it is not the active antivirus on this machine; nothing was changed, nothing uploaded).
- **Installer, verified on the final artifacts (safe game copies only; the live Steam installation was never read or written).** 0.34.1: silent per-user install (exit 0, no elevation, `HKCU` uninstall entry only, no `HKLM`, no service, no task, Start menu and desktop shortcut), window title and icon, no Electron menu bar, default Triple generation, Apply / Restore, clean quit, **silent uninstall verified** (folder, shortcuts and registry entry removed, user data kept). 0.34.2 (final): silent install (exit 0), version 0.34.2, mode installed, English and Polish audit of the main states, the DLC switch, a DLC-off test world created, applied (DLC rows identical) and restored (0 differences), three golden worlds reproduced byte for byte, no stale notice, clean quit. **Windows answered "access denied" to the first launch of a freshly installed unsigned executable** (the app on 0.34.2, which started a few seconds later, and the 0.34.2 uninstaller): another security product on this machine is the likely cause, unproven; nothing about the system security was changed. **PARTIAL for 0.34.2:** its uninstaller could not be executed at all (denied for the file in place and for a copy, over several minutes), so the uninstall was verified on 0.34.1 only (the same NSIS mechanism). The shortcuts and the uninstall entry of that test install were removed by hand; its folder `C:UsersAdamds1tr-qainstalled-2` is left behind. **STATICALLY VERIFIED:** the English installer wizard (Windows here is pl-PL and the installer follows the Windows language; no safe override exists).
- **Portable, verified on the final artifact (0.34.2; the Auto language on 0.34.1).** Launch with a fresh English profile, mode `portable`, version 0.34.2, generation without a stale notice, hint sheet, Apply and Restore on a copy (0 differences), clean quit; Auto language resolved to Polish on this pl-PL Windows (0.34.1). **Observed, not reproduced:** during the 0.34.1 portable run the interface language changed from English to Polish about a minute after the start (`app.log`: "language set to pl"); none of the scripts touched the language control and a repeat on 0.34.2 with a fresh profile stayed English for the whole run. Most likely a manual click on the visible window; reported as unexplained, not as a defect. **PARTIAL:** exporting the support bundle goes through the native save dialog, which was not driven; the bundle builder is covered by `supportBundle.test.mjs` and was exercised on the previous packaged build.
- **Gameplay QA plan (`docs/FINAL_GAMEPLAY_QA.md`).** 17 quick smoke tests and 8 targeted extended tests under their exact titles in the app, the DLC checks, six full-playthrough worlds as Seed Codes (Recommended Triple, Chaotic Triple, Fog with shuffled keys, Race Mode, Race Mode +, DLC content left out; each generates twice identically through the real pipeline), estimates, what to look for, pass / fail, and which worlds the owner already played. **GAMEPLAY UNVERIFIED** for everything not marked: nothing was played for this phase.
- **CORE FEATURE FREEZE.** From this version only blocker, correctness, data-loss, serious UX and security fixes. Not started, by decision: Seed Hub, auto-update, accounts, telemetry, more languages, more Race parity, more E2 bosses, more Part F NPCs, new modes. **Deferred list:** Polish names of game content (game content names stay English, `I18N.md`), more languages, the full legacy difficulty tables, Race destination parity, unsupported Race + skips, the Purple Coward's Crystal skip, extended Laundromat Mixup, broader E2 / Part F coverage, the DLC off with the fog randomizer (needs a FogMod change), a support bundle preview, Seed Hub, accounts, leaderboards, Daily / Weekly seeds, auto-update (telemetry: none).

**Product UX / support / localization - language (English / Polish), Hint Sheet, diagnostics, support bundle (2026-10-08; backend 0.44.2, contracts 0.30.2, desktop 0.32.3, FogHost project 0.6.0 with HostVersion 0.5.0, patch 0024; generation profile `ds1tr-gen-v1` unchanged; no generation change)**
- **Language** (`docs/I18N.md`): i18next + react-i18next, bundled `locales/en|pl/<area>.json` (13 areas, English canonical, keys type-checked), Auto / English / Polski resolved by the main process before the first render (saved choice, else Windows language; unsupported -> English), persisted in `settings.json`, switched without a restart. Backend errors are stable codes (about 70 translated), plan lines carry keys, the 40 test worlds (quick + extended) and the checklist are translated, dates/numbers follow the language. **The language is not in the Seed Code, the settings or any hash**; the Fog "game language" stays a world setting, relabelled *In-game text language*. Installer: `nsis.installerLanguages [en_US, pl_PL]`.
- **Hint sheet** (`docs/HINT_SHEET.md`): `hints.json` written once next to the run (never in the package); three levels (Low spoilers / Progression / Full spoilers, names only in the resources) as one pure filter; viewer with world summary, search and sections; HTML (self-contained, CSP, no script, escaped), TXT, JSON (`schemaVersion 1`, stable ids); old history entries rebuilt only from placements that still exist. With vanilla keys (the default) Low / Progression are empty by design and say so.
- **Diagnostics and logging** (`docs/DIAGNOSTICS.md`): `Diagnostics` model (versions, install mode, signing state from `build-info.json`, game state, world, package summary, fog retries, timings, event windows), structured JSON logs with the stages GAME_DETECTION ... RESTORE, correlation id per operation (diagnostics only), `diagnostics/last-run.json` that survives failures.
- **Support bundle** (`docs/SUPPORT_BUNDLE.md`): allowlist of 10 named entries, redaction of user / host / folders (every spelling) / secrets / tokens / addresses, leak scan that refuses a bundle, size limits (2 MiB total), ZIP read-back; works after a failed generation, a failed Apply and with no world.
- **History**: row menu has View hints, Export hints, View settings, Diagnostics, Export support bundle, spoiler, delete; "Restore and create world" keeps the old entry (restored), gives the new world its own hint data and diagnostics (backend regression test + packaged app).
- **Tests (AUTOMATED VERIFIED).** Backend 537 tests: 535 pass, 0 fail, 2 skipped (needs the real game); desktop 33/33 (`release-tools` included); root typecheck clean. New: `locales.test.mjs` (parity, plurals, variables, fallback, literal detector), `language.test.mjs`, `hintSheet` / `hintStore` / `hintData`, `redaction`, `supportBundle` (synthetic secrets searched in every ZIP entry), `operations`, `diagnosticsText`, `smokeLocale`, `planBehaviors`, `restoreCreateWorld`, `saveFile`. The seven UX source-pin tests were moved to the resources (key used + English text unchanged).
- **Golden fixtures (AUTOMATED VERIFIED).** `goldenCompatibility.mjs verify` on the clean game copy with the packaged FogHost and DsrDataTool: **47 / 47 PASS, byte for byte** (RESULT: PASS). The whole phase's diff in the generation path is two optional log tags (`component`) and the `appVersion` string; no engine file, no seed or settings code changed.
- **Same world in both languages (AUTOMATED, packaged app).** The same Seed Code generated twice (Polish UI, then English UI): every package file and hash identical (5 files compared; `randomizer.json` compared without `config.outputDirectory`, the workspace path, like the golden check; `config.json` / `patch-manifest.json` excluded for the same reason), same attempts.
- **Packaged-app checks (clean game copy only; the live installation was not touched).** Fresh profile on this pl-PL Windows: Auto resolved to Polish, English / Polski / Auto switched live, the choice survived a restart (no flash: `html lang` correct at first look). A scripted audit of the DOM found, after the 11 px font fix, **0 problems** in every checked screen in both languages (ready, advanced with diagnostics and test worlds, generated, hint dialog at each level and its export section, diagnostics dialog, applied, pending "Przywróć i utwórz nowy świat", history): no raw keys, no `[object Object]`, no text of the other language, no clipping, no text below 11 px, no horizontal scroll. Apply / Restore / Load seed while applied -> restore-and-create / history / hints of both worlds / diagnostics of the old world: all as expected. Real exports through the native save dialog (Enter pressed): the HTML (132,714 bytes; doctype, no script, CSP) and the support-bundle ZIP (12,202 bytes, the 10 allowlisted entries) were written; the ZIP contains no user name, no clean-game folder, no AppData path and not the live session token; the Seed Code is kept. The portable build logged `started (portable)`; a silent install of the installer started, reported `installed / unsigned / 0.32.3` and uninstalled. The installer wizard opened in **Polish** ("Instalator DS1 Triple Randomizer", "Wybierz opcje instalacji", "Dalej >", "Anuluj") on this Polish Windows.
- **Packaged builds (UNSIGNED).** Installer `apps/desktop/release/DS1-Triple-Randomizer-Setup-0.32.3.exe` (130,242,913 bytes, SHA-256 `b5ef88eb4d61c380f1297b9bd6c3fd4e919ff9210ed09440bc0014eba1de7308`), portable `DS1-Triple-Randomizer-0.32.3-x64.exe` (129,726,777 bytes, SHA-256 `e8dc219ab1b4ecfbbd7414fcaec05dc6e27fe23fae47f1e169b3b8c6d17772fe`); `release-verify`: RESULT OK for development mode (UNSIGNED; a public release still needs the owner's certificate, `docs/RELEASE_TRUST.md`).
- **Fixed on the way.** The first hint wording called boss-reward / gift / drop / shop slots by a boss or NPC; the engine names them by the item that lies there in the clean game (now said exactly); spells (equip type 4) were unhintable (16 per world) and are now a hint kind of their own; the diagnostics panel re-reads when the world changes; the 9 / 10 px labels are 11 px.
- **Not done / limits.** PARTIAL: preset and validator problem lines and a few dynamic engine messages stay English technical text under a localized lead; redaction is pattern- and known-value-based. NOT SEEN: the English installer wizard (Windows here is pl-PL; STATICALLY configured) and the live use of the exported hint sheets in the game (GAMEPLAY UNVERIFIED: nothing here was played). DEFERRED: Polish names of game content, more languages, a preview of the bundle before saving. Commit `fb6badc` also contained the owner's uncommitted one-line edit of `docs/AI_EXECUTION_PLAN.md`; commit `f28efd0` takes it back out of the committed file and the edit is in the working tree, uncommitted, as it was.

**Part I: final item randomizer feature-parity pass - Race Mode, Race Mode +, item Difficulty, Laundromat Mixup (2026-10-07; backend 0.41.0, contracts 0.27.0, desktop 0.29.0, FogHost project 0.6.0 with `HostVersion` 0.5.0; patch 0024; generation profile `ds1tr-gen-v1` unchanged; presets version 15). AUTOMATED VERIFIED on real data / STATICALLY VERIFIED; GAMEPLAY UNVERIFIED. All four features are SAFE SUBSETS, none claims exact legacy parity. Builds: UNSIGNED.**
- **Audit first** (`docs/RACE_MODE.md`, `docs/ITEM_DIFFICULTY_MODEL.md`, `docs/LAUNDROMAT_MIXUP.md`, committed alone): the twenty questions per option from the pinned legacy sources (`HotPocketRemix/DarkSoulsItemRandomizer` `f7cabbd`, behavior only). Race Mode = Shuffled with the key candidates limited to the race locations; Race Mode + = the same plus skip edges, no prison check, no Sen's rule, the Purple Coward's Crystal as a key (the GUI forces Fair); Difficulty = an ordered preference of location tiers per item tier (keys HARD, small souls EASY, big and boss souls HARD), Fair = uniform = the legacy default; Laundromat = the armor cells of the NPC rows of CharaInitParam only (the starting classes are not part of it).
- **Race and Race +** (strategies of the one key planner, `KEY_PLACEMENT_STRATEGIES`): race destinations derived from the game data (boss rewards, chests = treasure events with the chest model read by the scan, vanilla pickups of keys and embers; 69 destinations, 87 of the 103 legacy rows agree); the embers, the Rite of Kindling, the Sewer Chamber Key and the Mystery Key are race items (stream `item-race`); Race + uses the vanilla graph plus 11 named skip edges of FogMod's glitched logic (Capra, Lower Burg, Annex jump, Seal skip, Four Kings OOB, crystal edges); 10.3% of Race + worlds need a skip (0% of Race). Excluded and documented: Black Knight and other enemy drops, Andre / Merchant / Ingward rows, the Golem drop, DLC boss drops, the Orange Charred Ring reward, the skips FogMod does not model. With the fog randomizer Race + is made as Race Mode (measured `FOG_KEY_CONFLICT` otherwise).
- **Difficulty** (`difficulty.js`): the policy is exact; the tiers are DS1TR-derived from the game data (agreement with the legacy tables 40% / 39%, chance 33%); measured distribution (200 worlds per mode): HARD items in HARD locations 14.1% (Fair) -> 32.8% (Unfair) -> 50.1% (Very Unfair), HARD items in EASY locations 38.2% -> 3.9% -> 3.9%, EASY items in EASY locations 40.6% -> 81.6% -> 81.6%. No mode can make a world unwinnable. A guard keeps "random only" items (Cast Light) out of the area they open.
- **Laundromat Mixup** (`laundromat.js` + DsrDataTool + `SmokeVerify laundromat`): 60 NPC characters wear pieces other NPCs already wear (no computed id), hollowed variants untouched; 25,757 planned rows read back from written packages (Fog included), 0 differences from the clean game outside the plan.
- **Compatibility.** Extension fields only (`race`, `race-plus`, `itemDifficulty` neutral `fair`, `laundromat` neutral off); presets version 15; **all 36 earlier golden worlds regenerate byte for byte** with the final engine, helpers and FogHost; 11 new `item-modes` worlds recorded and regenerate. Zero RNG when neutral (`combinedItemDifficulty.test.mjs`).
- **Proofs.** Engine level (`itemModesSweep.mjs`, 3,800 worlds, independent audits): 0 failures. Real data tool and FogHost (`placementMatrix.mjs item-modes-h`): **645 worlds, 0 failures, 0 non-deterministic reruns, 1 key placement attempt each, 0 fog retries**. Backend 483 tests (481 pass, 2 skipped by design), typechecks clean.
- **Hint-sheet data** (`core/hintData.ts`): one record per placement (item, kind, importance, original and new location with acquisition type and semantic owner), readiness test over generated worlds; no hint sheet is built; no spoiler severity exists in the architecture.
- **UI.** Items card: Key placement (Vanilla / Shuffled / Race Mode / Race Mode +), the Lordvessel and Lord Soul switches, "Item difficulty" group (Fair / Unfair / Very Unfair; disabled and forced to Fair under Race Mode +), Laundromat Mixup toggle; the "planned" group is gone (nothing planned is left in the card); the pipeline preview lists Key items, Item difficulty and Laundromat Mixup.
- **Quick test worlds (GAMEPLAY UNVERIFIED, statically verified):** `quick-race`, `quick-race-plus`, `quick-fair`, `quick-unfair`, `quick-very-unfair` (item lot 1810080 in the first cell, 2.8 m from the start) and the extended `laundromat-npcs`.
- **Packaged builds (UNSIGNED; clean game copy only, never the live installation).** Installer `apps/desktop/release/DS1-Triple-Randomizer-Setup-0.29.0.exe` (130,225,051 bytes, SHA-256 `cd75e76e8d93cfcccd2abf13fe184dab05d41534d5f355ff1733978aa72d7a3d`) and portable `apps/desktop/release/DS1-Triple-Randomizer-0.29.0-x64.exe` (129,635,344 bytes, SHA-256 `4e810be2c181034f10d63a4dff29eb0062ab38561d6c92f37e60f6f9f1a92ac2`); `release-verify`: RESULT OK for development mode, UNSIGNED, 146 files; desktop tests 14/14. Packaged application (fresh profile, its own UI): all the new controls (Race Mode + forces and disables Fair, leaving the key modes clears the switches, the planned group is gone), a full Triple with Race Mode + / Laundromat (planned as Race Mode with the fog randomizer), Apply and Restore, Load seed while the game is applied (a Race Mode / Very Unfair code: the new world is created with the settings kept, one extra history entry, no duplicate), Load seed of an old golden code (Vanilla / Fair / Laundromat off), the settings after a restart. **Installer:** silent per-user install exits 0, one HKCU uninstall entry, the installed application starts; the silent uninstaller exits 0 and leaves no folder and no entry. **Portable:** starts. Not re-checked in this phase: the icon, the title image and the missing Electron menu (unchanged files).
- **Remaining item parity gaps:** the legacy's hand-rated tiers (exact Difficulty), the skips FogMod does not model, Black Knight / shop destinations in Race, the Purple Coward's Crystal as a movable key, the hat chances and flagged sets of the Laundromat, keys in shops; the hint sheet itself.

**Part H: item and progression parity - Soul items Transposed, key placement, Lordvessel and Lord Souls (2026-10-07; backend 0.40.0, contracts 0.26.0, desktop 0.28.0, FogHost project 0.6.0 with `HostVersion` 0.5.0; patches 0021 to 0023; generation profile `ds1tr-gen-v1` unchanged; presets version 14). AUTOMATED VERIFIED on real data / STATICALLY VERIFIED; GAMEPLAY UNVERIFIED. Builds: UNSIGNED.**
- **Soul items Transposed** (backend 0.39.0): the legacy semantics from the pinned sources and the game data; boss reward catalog (event 970; ownership follows the encounter flag, never a body); 380/380 real-data worlds; 4 golden worlds; a quick test world. `docs/ITEM_PROGRESSION_MODEL.md` sections 1 to 4.
- **Key placement** (`items.keyPlacement`: `vanilla` / `shuffled`): one world model (FogMod's graph exported by `FogHost progression-model`, evaluated by `keys.js`, cross-checked against FogMod's `GraphChecker` on 3000 assignments), assumed fill over certain world and boss pickups, displaced items paired with the freed pickups, bounded and deterministic (streams `item-keys:<n>`, `item-keys-fill`), typed failures (`KEY_PLACEMENT_*`, `FOG_KEY_CONFLICT`), works with the fog randomizer off and on. **Lordvessel and Lord Souls** are two switches inside it (no event is rewritten). Race Mode / Race Mode +: UNSUPPORTED. DLC: decision C unchanged (`includeDlc` fixed on; no key in the DLC).
- **Compatibility.** The new options are extension fields (written to a Seed Code only when non-neutral); every earlier Seed Code, preset and settings record keeps its text and meaning; all 31 earlier golden worlds regenerate byte for byte; 5 new `key-placement` worlds recorded. Neutral engine runs are byte-identical.
- **Proofs.** `placementMatrix.mjs keys-h`: **980 worlds, 0 failures** (510 without Fog, 250 with Fog, 220 full Triple; 460 of them with Lordvessel / Lord Souls), 1 key placement attempt in every world, 0 `FOG_KEY_CONFLICT`, 0 unsatisfiable, 0 duplicates or missing items, 0 non-deterministic reruns; FogMod's reader and checker over the lots of every written package. One measurement artifact (Cast Light, a `randomonly` item) found, explained and corrected in the script (section 9). Backend 461 tests (459 pass, 2 skipped by design), typechecks clean.
- **UI.** Items card: "Key items" group (Key placement Vanilla / Shuffled, Lordvessel placement, Lord Soul placement; the two switches are cleared when Key placement leaves Shuffled), "Legacy parity planned" keeps Difficulty, Race modes ("Not built") and Laundromat Mixup; the pipeline preview lists Soul items, Key items, Lordvessel and Lord Souls.
- **Quick test worlds (GAMEPLAY UNVERIFIED):** `quick-transposed-soul`, `quick-key-placement` (Cage Key in the cell), `quick-lordvessel`, `quick-lord-soul`; each names the exact pickup (item lot 1810080, 2.8 m from the start), the item expected there and what to check; static check `smokeTestCatalog.mjs verify` passes.
- **Packaged builds (UNSIGNED; checked against the clean game copy, never the live installation).** Installer `apps/desktop/release/DS1-Triple-Randomizer-Setup-0.28.0.exe` (130,206,997 bytes, SHA-256 `4bbbf59e668a6ba2111c51da28b1c17bca4124efb59835fa9f7142f426c3c52d`) and portable `apps/desktop/release/DS1-Triple-Randomizer-0.28.0-x64.exe` (129,617,225 bytes, SHA-256 `ce6558e00ee84e7f65fc906769fe50b14404f05a84a382f9979ffe0a1ccf967f`); `release-verify`: RESULT OK for development mode, UNSIGNED, 143 files, no elevation helper; desktop tests 14/14. Packaged application (`win-unpacked`, fresh user data folder, driven through its own UI): the Key items group, the switches (inactive and cleared when Key placement is Vanilla), the warning without World items and the plain refusal at Randomize, a full Triple with Key placement + Lordvessel + Lord Soul placement (the Seed Code decodes to `keyPlacement shuffled`, both switches on), Apply and Restore, the pipeline preview lines, Load seed of an **old golden code** (Vanilla, switches off) and of the keys code (Shuffled, both on), and the settings after a restart. **Not repeated in this phase:** the silent installer install/uninstall and the portable start (they were exercised on the previous builds; the contents are the same packaging path).
- **Remaining parity gaps:** Race modes, Difficulty classes, Laundromat Mixup, the hint sheet (data exists), excluding the DLC, keys from enemy drops / NPC drops / the Orange Charred Ring pickup. Next phase recommended: play the four quick worlds and one full run of a keys world; then Difficulty or the hint sheet.

**Parts A + G + Release trust (2026-10-07; backend 0.38.0 (0.37.1: event owners), contracts 0.24.0, desktop 0.26.0; patch 0018; generation profile `ds1tr-gen-v1` unchanged; RANDOMIZER_VERSION 0.5.66 and FogHost 0.5.0 unchanged; presets version 13). Event owners: AUTOMATED + STATICALLY VERIFIED. Default flip: AUTOMATED VERIFIED. Builds: UNSIGNED (SIGNING READY, never TRUSTED SIGNED).**
- **A. Darkroot Basin event owner.** The Basin (`m12_00_00_01`) has no event file of its own: its EMEVD is the Garden's (`m12_00_00_00`). Proven from the game data by a one-off analysis of a clean copy (every MSB's entity IDs against every event file; the analysis tool was a scratch program and is not in the repository, the finding is recorded in `docs/EVENT_ALLOCATION.md`), guarded in the repository by `EnsureEventOwnersExist` and `SmokeVerify audit`, resolved centrally in DsrDataTool (`EventOwnerMap`, `EnsureEventOwnersExist`, `docs/EVENT_ALLOCATION.md` "Event owners"), event IDs allocated per owner file, ten event filters use the owner, the catalog event source is de-duplicated. Mode `shared` is chosen by the backend for legacy-style sampling worlds; **classic stays frozen** (its Basin placements keep having no events: a documented quirk). Hostile NPCs may now stand in the Basin (the restriction is gone). Verifier: `SmokeVerify audit` and the matrix fail on any placement whose events are missing from its owner file. **Recorded exception 2** in `docs/GENERATION_COMPATIBILITY.md` (12 sampling + 5 hostile fixtures re-recorded; every classic and unaffected fixture byte-identical). Patch `0018-event-owners.patch`.
- **B. Part G, the default.** New configurations start with `sampling-v1` and the legacy defaults ("Sampling (recommended)" / "Classic permutation"; serialized values unchanged); Seed Codes, presets, saved settings and history keep their algorithm; the test worlds use the frozen `SMOKE_TEST_BASE_SETTINGS`; no dead controls (the sampling options are shown only with sampling; the "planned" placeholder is gone). Ordering of the planners is documented and locked by tests (`enemyPlacementOrdering.test.mjs`); a neutral option draws nothing. The placement validation summary is recorded in the run manifest (`enemyPlacement.validation`). Details: `docs/ENEMY_PLACEMENT_MODEL.md` 10.10, `docs/SEED_FORMAT.md` "Defaults and old records", `docs/DECISIONS.md`. **Status wording: Enemy Placement core complete with documented safe-subset deviations.**
- **C. Release trust and antivirus hardening.** `docs/RELEASE_TRUST.md`, `docs/SECURITY_AND_PRIVACY.md`, `tools/release/` (policy, verifier, public-release gate, optional Defender scan, helper format experiment, tests). Installer (per-user NSIS, no administrator, no elevation helper) is the primary download, portable the secondary; helpers are an **uncompressed** single file (entropy 6.71 instead of 7.81 bits per byte; start time DsrDataTool 1,054 to 373 ms, FogHost 315 to 384 ms); executable metadata names this project, not the toolchain; `npm run package:win -- --release` refuses to run without credentials from the environment (tested), signs the application, both helpers, installer, uninstaller and portable when it has them, and ends with `release-verify --require-signature`. **Pipeline tested with a throwaway certificate in a scratch folder** (all five DS1TR files signed by electron-builder; the verifier says `TEST-SIGNED (untrusted root)` and fails public mode; the certificate and the signed output were deleted). A bug found by that test: `signingHashAlgorithms` must be an array (moved to `electron-builder.yml`). No certificate exists: **every build is UNSIGNED.** The tooling folder was previously ignored by `.gitignore` (`release/`): fixed with an explicit exception, and `*.pfx`, `*.p12`, `*.pem`, `*.key`, `.env` are now ignored.
- **Proofs (clean copy of the game only; the live Steam installation was not touched).**
  - Real data tool, event owners (`placementMatrix.mjs event-owners`): **810/810** (E1 roaming + both stay dead, hostile NPCs, Type replacement enabled / except roaming, general sampling: 100 seeds each; Part D boss sampling, E2 bodies, Second Gargoyle: 60 each; classic frozen: 30): 0 failures, 0 event owner failures over about 230,000 checked events, 0 non-deterministic reruns, 0 window overruns.
  - Real data tool, the product default (`placementMatrix.mjs product-default`): Items + Enemies + Bosses 100/100; Enemies + Bosses 100/100; **the full Triple with the real FogHost 100/100**.
  - Fog real format (`fogHostE2e.mjs`, `verify-skips`): default, E1 both + stay dead, hostile NPCs + Type enabled, E2 boss bodies: 4 x 25 = **100/100**, 0 retries, 0 conflicts, 100/100 skip checks, 0 event mismatches (including Basin worlds).
  - Engine sweeps: default 500/500 and classic 500/500 (`defaultSweep.mjs`); feature matrix 384 combinations x 30 seeds (`featureMatrix.mjs`): 7,680 worlds, 3,840 typed refusals, 0 failures.
  - Golden: **27/27** regenerate from their Seed Codes.
- **Packaged builds (UNSIGNED; all checked against the clean game copy, never the live installation).** Installer `apps/desktop/release/DS1-Triple-Randomizer-Setup-0.26.0.exe` (130,184,556 bytes, SHA-256 `ead503ab536b1984cb614d64060934926432da38be912416fe2238bfc9cfd02b`) and portable `apps/desktop/release/DS1-Triple-Randomizer-0.26.0-x64.exe` (129,594,845 bytes, SHA-256 `c97e4251b827fce60d0a1d605720790a4de204e8a4fa74a900901854b78f2a85`); `release-verify` reports UNSIGNED, structure and metadata OK, no `elevate.exe`. In the packaged app with a fresh user data folder: the algorithm select says "Sampling (recommended)", all sampling controls are present, Randomize makes a full Triple (Items + Enemies + Bosses + Fog) whose Seed Code decodes to `sampling-v1`, Apply and Restore succeed, after a restart the settings and history persist (one entry, "Restored"). An **old classic Seed Code** (golden `enemies-classic-permutation`) loaded with Load seed makes that world with "Classic permutation" selected and the sampling controls hidden; after a restart the classic choice persists; Randomize with it makes a code that decodes to `permutation-v1`; "Restore default settings" returns to sampling. **Installer:** silent per-user install exits 0 without an elevation prompt, writes the install folder, one desktop and one Start menu shortcut and one HKCU uninstall entry (no HKLM, no `elevate.exe`); the installed application generates a full Triple, Applies and Restores; the silent uninstaller removes the folder, shortcuts and the entry and keeps the user data. **Portable:** starts and passes the same checks; it runs from a random folder under `%TEMP%` (self-extraction, as documented). The UI/installer checks above were run on the previous build of the same sources (backend 0.37.1 -> 0.38.0 changed only the version strings and the final package was rebuilt from the committed tree and re-verified with `release-verify`; the UI checks were not repeated on it). Local Defender scan: NO RESULT (Defender is not the active antivirus here). **Norton 360 blocked the two throwaway-signed test copies of the helpers** (`IDP.HELU.PSD11`); it did not block any unsigned packaged file in this session (`docs/RELEASE_TRUST.md` section 8).
- **Tests:** backend 425 tests (423 pass, 2 skipped by design, 0 fail), desktop 13/13 (includes the release tooling tests), both typechecks clean. Found and fixed on the way: the integration test picked a random port that can fall into a range Windows reserves (it now asks the operating system for a free port), and two smoke-test assertions that predated the recorded gameplay results.
- **Gameplay status (honest).** *Runtime-verified by the owner:* the main E1 quick tests, boss sampling (first boss), sampling first enemies, E2 Asylum Demon, E2 Taurus Demon. *Not verified by playing:* Part F (hostile NPCs), E2 Capra Demon, the extended Fog / Gargoyle / Parish worlds, the Darkroot Basin test worlds. **No gameplay was claimed or inferred for any of them.**
- **Not done (by design):** no certificate was bought, requested or generated for release; nothing was pushed, published or uploaded to a scanner; the local Defender scan has no result (Defender is not the active antivirus on the machine); no setting of the security software was changed.

**Part F1: hostile NPCs, a safe subset (2026-10-07; backend 0.37.0, contracts 0.23.0, desktop 0.25.0; patch 0017; generation profile `ds1tr-gen-v1` unchanged; RANDOMIZER_VERSION 0.5.66, FogHost 0.5.0; presets version 13). PART F SAFE SUBSET - STATICALLY VERIFIED / GAMEPLAY UNVERIFIED.**
- **What it is.** The legacy "Hostile NPCs" option: the Undead Merchant (Undead Burg), Andre of Astora and Vamos join the pool of normal enemies as hostile fighters (a toggle "Hostile NPCs", shown only where it can apply). The real NPCs are never touched. The legacy "NPC replacement" (E3) is **not built, by decision**: its six slots are services (shops, blacksmiths,
  the Fair Lady) and replacing them removes them; the placeholder control was removed. The legacy has no hostile destination slots, NPC-to-NPC rule, invaders or summons (`c0000` is in no table). Reconstruction of the 20 questions, the classification of every non-regular slot and the cell ownership table: `docs/ENEMY_PLACEMENT_MODEL.md` 10.9.
- **Safety.** Three of the legacy's four sources (Gough and the Lower Burg Merchant are left out, with reasons). The slot owns the actor: team, `npcType`, covenant mark, respawn, drops, humanity, item lots and souls come from the slot's row (asserted by the data tool); the body, animations and AI are the NPC's. No talk script is read, written or present in any package. At most twelve per map (an uncapped run had up to 117 in one map: the cap bounds the wake-up events),
  none in the Darkroot Basin (no event file there; found by the verifier), no wake-up for dummy parts, the Lower Burg Merchant's missing logic script (found by the verifier) is why it is not a source.
- **Compatibility.** `hostileNpcs` is an extension key (neutral false); no new stream, no new constant; the option off adds nothing: all 22 earlier golden fixtures verify byte for byte; 5 new fixtures (`ds1tr-gen-v1.hostile-npcs.json`). No Seed Code changed.
- **Quick test** `quick-hostile-npc` ("Hostile NPC - Andre as an enemy", `smoke-q8-npc-562`, about 30 s): Andre of Astora in the first Hollow's place, 8.7 m from the start. **Not played.** Five extended worlds: `npc-world`, `npc-fog`, `npc-roaming`, `npc-type`, `npc-bosses`.
- **Proofs (real catalog / data tool / FogHost; clean game copy only; the live Steam installation was not touched).**
  - Engine `npcSweep.mjs`: 15 configurations x 100 seeds = 1500/1500. E1 `roamingSweep.mjs` 1800/1800 and E2 `bodySweep.mjs` 1800/1800 unchanged with the option off.
  - Fog (real format, `verify-skips` on every package): **100/100**, 0 retries, 0 conflicts, 0 skip mismatches; every overlay inside the existing 15-event whitelist, the same 12 merges: no new overlay.
  - Static verifier: 29 checks for the quick world; a 100%-replacement world passes 41/41 expectations (rows equal the slot's, no ghost transition, think row, battle and logic script in the map's AI bundle, wake-up event).
  - Real data tool matrix (`placementMatrix.mjs hostile-npc-f1`): 10 cases x 40 seeds (E1 used 100 and 60): **399/400** on the first run and 400/400 with the one exception rerun: seed 36 of `npc-vanilla-scaling` failed once with a Windows file-lock error while the data tool wrote a map file (`The process cannot access the file ... m13_02_00_00.msb because it is being used by another process`, an environment error, not a generation error) and passed when run alone (`--only 36`). 0 retries, 0 event-window overruns, 0 non-deterministic reruns; at most 108 custom events in one map (Demon Ruins, Type replacement except roaming bosses + roaming bosses; the window moved off the Bed of Chaos floor event in 3 seeds, which is what `docs/EVENT_ALLOCATION.md` provides); maximum model count 30 (31 in a map whose fixed models already exceed 30, the non-worsening rule).
- **Tests:** backend 411 tests (409 pass, 2 skipped by design, 0 fail), desktop 6/6, desktop typecheck (both tsconfigs) clean, `goldenCompatibility.mjs verify` 22/22 earlier fixtures plus the 5 new ones recorded after, `smokeTestCatalog.mjs verify` for the quick worlds..
- **Packaged build:** fresh portable EXE `apps/desktop/release/DS1-Triple-Randomizer-0.25.0-x64.exe` (105,550,117 bytes, SHA-256 `43601824a7bb1ea2c8e3ef7c3223a02582a1e910e347d17d656a3b01c584cc08`). In the packaged app: custom icon and no menu bar (`HMENU` 0), a decorative brand mark in the header (withdrawn in 0.35.0), run history collapsed, the game folder remembered after a restart, the "Hostile NPCs" toggle exists only with legacy-style sampling and normal enemies replaced (`normals` / `both`) and no custom pool, goes back to off when the mode is left, and the disabled "NPC replacement" / "Hostile NPCs" placeholders are gone (only "Allow T-pose behavior" is still planned); the quick tests are grouped under "Quick smoke tests" and the new one shows target / expected / time / checks and "not played"; "Create this test world" and Load seed generate the world automatically, the Seed Code equals the one shown, and the loaded settings switch the toggle on; the package the packaged app produced passes the static checks..
- **Not verified by playing:** everything runtime for a hostile NPC (wake-up, hostility, no T-pose, death, only the slot's rewards, respawn like a regular enemy). **E2 was not changed**; the user's E2 tests are separate.

**Part E2: a regular enemy as the body of a boss encounter, a safe subset of three (2026-10-07; backend 0.36.0, contracts 0.22.0, desktop 0.24.0; patch 0016; generation profile `ds1tr-gen-v1` unchanged; RANDOMIZER_VERSION 0.5.66, FogHost 0.5.0 and presets version 12). STATICALLY VERIFIED / GAMEPLAY UNVERIFIED.**
- **What it is.** Boss replacement gains "Only normal enemies (early fights)" (`normals`) and "Bosses or normal enemies" (`both`, with "Boss chance in boss slots", default 90, shown only then). A normal enemy stands in the arena of the **Asylum Demon,
  Taurus Demon or Capra Demon**; the encounter stays the boss's (entity, events, fog gate, what the death unlocks). Every other boss keeps its boss (`normals`) or gets a boss (`both`). This is a subset on purpose: the audit of
  all 24 boss encounters (class and reason for each exclusion) and the reconstruction of the legacy semantics (20 questions) are `docs/ENEMY_PLACEMENT_MODEL.md` 10.8. **Not claimed:** "full E2".
- **Design (decision in `docs/DECISIONS.md`).** Entity-preserving body through the existing Part D boss path: no new event, no new event ID, no new Fog overlay; the body's scaled NpcParam row takes the encounter-owned cells (`dropType`,
  `getSoul`, `disableInitializeDead`, item lots) from the boss row and sets `disableRespawn` 1 (`EncounterOwnedCells`, data tool patch 0016). The health bar keeps the boss's name. Scaling follows the existing setting. Streams `category:slot:<id>` and
  `body:slot:<id>` are new; with the chance at 100 `both` equals boss sampling.
- **Compatibility.** `bossChanceInBossSlots` is an extension key (neutral 90); presets version 12. All 18 earlier golden fixtures verify byte for byte (re-verified after the last engine change); 4 new fixtures (`ds1tr-gen-v1.encounter-bodies.json`: basic,
  Fog, `both` + roaming bosses, Custom Pool edge). No Seed Code changed.
- **Quick test** `quick-encounter-body` ("First boss fight - a normal enemy", `smoke-q7-body-43`, about 90 s): Balder Knight in the Asylum Demon arena, 22.5 m from the start, 13.3 m up; the health bar must say "Asylum Demon", the knight takes boss health,
  the fight completes and the way on opens. **Not played.** Five extended worlds: `body-burg-fights`, `body-fog`, `body-roaming`, `body-both`, `body-gargoyle` (`docs/SMOKE_TESTS.md`, regenerated).
- **Proofs (real catalog / real data tool / real FogHost; clean game copy only; the live Steam installation was not touched).**
  - Engine: `bodySweep.mjs`, 18 configurations x 100 seeds = 1800/1800 (bodies only in the audited slots with the audited models, entity ID and scaled row, scaling as set, model count never above `max(30, vanilla)`, `both` at 100 = boss
    sampling, `both` at 0 = the bodies of `normals`, no body outside a Custom Pool). E1 regression `roamingSweep.mjs`: 18 configurations x 100 seeds = 1800/1800.
  - Fog (real format, `fogHostE2e.mjs`, `verify-skips` on every package): **100/100** generated over 10 configurations x 10 seeds (`normals`, `both`, curve/fits/anywhere, replacement chance 60-100, Unique bosses, Type replacement enabled / except
    roaming bosses, roaming bosses at the same time, Custom Pool), 0 retries, 0 conflicts, 0 skip mismatches over 11,526 edited events / 33,914 surviving skips, **every overlay ID is inside the 15-event boss overlay whitelist (no new overlay)**; the maximum
    model count is 33, the Undead Parish baseline.
  - Static verifier (`tools/smoke-verify`, 23 checks per encounter): read back from the real package of one world with a body in all three slots, **all three slots pass** (entity ID, rows equal the boss row, respawn bit, events kept, no condition / flag-write /
    initializer removed; the only removed instructions are the model-specific forced animations `2003[18]` aimed at the encounter's own entity, which is Part D's sanitization: the Asylum Demon drop and the Taurus Demon entrance).
  - Real data tool matrix (`placementMatrix.mjs encounter-e2`, 10 cases x 40 seeds; E1 used 100 and 60): **400/400** generated (10 cases: `normals` anywhere / fits + curve / chance 50, `both` 40% / Unique, + roaming bosses, Type replacement enabled, Custom Pool, Vanilla scaling, Second Gargoyle + Gwyn strong), 0 retries, 0 window overruns, 0 non-deterministic reruns, maximum model count 30, at most 86 custom events in one map (Demon Ruins, Type replacement) against the 100-ID window.
- **Tests:** backend 405 tests (403 pass, 2 skipped by design, 0 fail; one server-start timeout was seen once while the Fog sweeps were running, it passed alone and in the final run), desktop 6/6, desktop typecheck (both tsconfigs) clean, `goldenCompatibility.mjs verify` 22/22 after the last engine change, `smokeTestCatalog.mjs verify` 7/7 quick worlds.
- **Packaged build:** fresh portable EXE `DS1-Triple-Randomizer-0.24.0-x64.exe` (built twice; the proof is on the second build). In the packaged app: custom icon and no menu bar (`HMENU` 0), a decorative brand mark in the header (withdrawn in 0.35.0), run history collapsed, the game folder remembered after a restart, Boss replacement offers the four modes, "Boss chance in boss slots" exists only with "Bosses or normal enemies" and returns to 90 when the mode is left (Unique bosses is reset when it no longer applies), the quick tests are grouped under "Quick smoke tests" and the new one shows target / expected / time / checks and "not played"; "Create this test world" and Load seed generate the E2 world automatically and the Seed Code equals the one shown (`...TMPK9NTD-S97TKN1N-...-1450`); the package the packaged app produced passes the 23 static checks.
- **Not verified by playing:** everything runtime for a body in the three arenas (wake-up, killability, health bar name, the way on, rewards, no respawn, crashes).

**Runtime smoke-test worlds (2026-10-06; backend 0.33.0, desktop 0.20.0, contracts 0.18.0).**
Advanced has a collapsed "Test worlds for playtesting" panel: eight fixed, reproducible worlds (classic full Triple, sampling of regular enemies,
the second Bell Gargoyle, the Undead Parish, boss sampling, a boss repeated in several places, Unique bosses, partial boss replacement) with a
Seed Code, what to look at in that world and a checklist (model, AI, navigation, floor, health bar, kill, fog exit, progression, rewards, respawn,
crashes). They are created through the normal Load seed path and pinned to `ds1tr-gen-v1`. `docs/SMOKE_TESTS.md` is generated from the same data
(`node apps/backend/scripts/smokeTestCatalog.mjs docs docs/SMOKE_TESTS.md`) and a test fails when a documented code differs from the app's.
Nothing is checked automatically and nothing is marked as passed: the worlds exist so a person can play them. The main screen now starts from
the same `DEFAULT_GENERATION_SETTINGS` as the test worlds (one definition in contracts).

**The game folder you chose is remembered (2026-10-06; desktop 0.19.1, contracts 0.17.0).**
After a restart the app used the Steam copy whenever Steam detection found one, even if the person had chosen another folder, because
the saved choice was honoured only when it was among the detected installations (and a typed folder was never saved at all). Now a folder
chosen by hand (Browse, "Use this folder", or one of several detected copies) is saved after the installation check accepted it and wins at
the next start; Steam detection is only the fallback when nothing is saved, the saved folder is gone, or it no longer passes the check.
The saved choice is machine-local (`userData/settings.json`), never part of a Seed Code, a preset or a generation configuration, and the
per-installation state is keyed by the installation path (`state/installs/<id>`), so state of one copy is never applied to another.
The decision is a pure function (`decideStartupInstallation`, contracts) with tests; a packaged-app proof drove the cases through the UI.

**Fix: custom event window vs the Bed of Chaos floor event (2026-10-06; backend 0.32.1, data tool only; `ds1tr-gen-v1` unchanged).**
`sampling-v1` with Type replacement `enabled`, Placement `anywhere` and bosses on sometimes failed in `patch-enemies` with
`Randomizer event ID is already used: 11419980`. Root cause (reproduced deterministically, details in `docs/EVENT_ALLOCATION.md`): the
per-map window of randomizer events (`x19900`...) is chosen before the fixed-ID Bed of Chaos floor event `11419980` is added, so a
Lost Izalith with more than 80 event-requiring placements (96 in the reproduction) ran into it. The floor event ID is now reserved before the
window is chosen (`FindFreeCustomEventRange(..., reserved)`), and the tool asserts that no map uses more IDs than were reserved. Windows of up
to 80 events, and every other map, are unchanged: the audit shows `IDs used == IDs reserved` in every map of every run.

- **Before / after on the same seed:** index 59 of the 100-seed set (and `golden-v1-boss-combined`) fails with the old tool exactly with
  the 11419980 message and succeeds with the fixed one (window moved to `11419981`). 1 of 100 seeds triggers it in this configuration.
- **100 seeds** of the formerly failing configuration, isolated run: 100/100 success, first attempt, deterministic reruns, 0 window overruns.
  A first run of the same 100 seeds, made while other heavy jobs ran in parallel, had one `DATA_TOOL_FAILED` (index 26) whose details were not
  captured; it did not recur in the isolated run, in three more runs of the same seed, nor in 52 runs under four-fold contention. Not
  explained; the matrix runner now records the tool's message for every failure.
- **Fog:** 30 seeds Type enabled + anywhere + bosses + Fog: 30/30, `verify-skips` clean on every package, the same 15 boss overlay events.
- **Compatibility:** all 14 earlier golden fixtures, the 24 lock hashes and the sampling pins are identical with the fixed tool; one new
  permanent fixture (`ds1tr-gen-v1.event-window.json`) pins the relocated window. A colliding run ended in `DATA_TOOL_FAILED` (one attempt,
  no reseeding), so no package or Seed Code of such a world ever existed; no profile change is needed.
- **Tooling:** `apps/backend/scripts/placementMatrix.mjs` (configuration x seed matrix with window audit, retries, deterministic reruns).

**Roaming boss respawn fix and the first real gameplay results (2026-10-06; backend 0.35.1, contracts 0.21.0, desktop 0.23.0; patch 0015; generation profile `ds1tr-gen-v1` unchanged).**
- **Gameplay milestone.** The five quick smoke tests (roaming boss - basic, roaming boss - souls and respawn, three roaming bosses at once, regular enemies - first three change, boss
  sampling - the first boss) were **played by hand in the real game and passed**: the replacements appeared and rendered, woke up, fought and could be killed, boss and non-boss health bars
  behaved as expected, the souls were as expected, the stay-dead boss stayed dead after the reset, the Asylum Demon encounter completed with the replacement boss, no crash or T-pose.
  This is real gameplay validation of Part D boss sampling on the Asylum Demon, of sampling-v1 regular enemies, and of roaming bosses with stay dead **on**. The extended worlds (Fog,
  Second Bell Gargoyle, Undead Parish, Type + roaming in the Burg, ...) were **not** played and are not marked as verified. Two of the five worlds (`quick-roaming-basic`,
  `quick-roaming-repeated`) were played with the package from before the respawn fix and have not been played again; their record says so.
- **Respawn bug fixed.** Root cause: a roaming boss row is a copy of a boss row (`disableRespawn = 1`) and DsrDataTool only set the bit when stay dead was on, so "stay dead off" never
  cleared it. The tool now writes the bit for every roaming row from the option (1 or 0) and asserts it. Compatibility: only roaming worlds with stay dead off change, and only in that
  cell of `GameParam`; the stay-dead-on fixture, the 15 earlier fixtures and Part D are byte-identical; recorded as a narrow exception without a new profile
  (`docs/GENERATION_COMPATIBILITY.md`). The UI hint "Off: it respawns like a normal enemy" is now true in the data.
- **New quick test `quick-roaming-respawn`** (Capra Demon in place of the Hollow c2500_0007, 11.4 m from the start, stay dead off): kill it, reset the area by dying once, return; the boss must
  be alive again. Statically verified (`disableRespawn = 0`); **not played yet**.
- **Regression (real catalog, real data tool):** golden 18 fixtures: 16 identical, the 2 stay-dead-off roaming cases changed exactly as predicted (`GameParam` only) and were re-recorded, then 18/18; quick static verification 6/6 (packages identical on a second run);
  Part E representative matrix 125/125 (5 cases x 25 seeds), Part D baseline 200/200 (boss sampling and curve, 100 seeds each, same figures as before), Fog 30/30 with `verify-skips` clean (15 boss-overlay events, 12 merges, 0 conflicts);
  0 event-window overruns, 0 non-deterministic reruns, no map above its own model baseline (Undead Parish 33 as before). Backend 397 tests, desktop 6/6.

**Smoke tests redesigned: quick vs extended (2026-10-06; backend 0.35.0, desktop 0.22.0, contracts 0.20.0; engine, data tool and generation profile `ds1tr-gen-v1` unchanged).**
The test worlds under Advanced are split into five **quick smoke tests** and twelve **extended gameplay tests**. A quick test puts the feature next to where the player wakes up
in the Undead Asylum (first visit): a Capra Demon where the first Hollow stood (8.7 m), a Taurus Demon with 100% souls that stays dead, the same Smough in three neighbouring slots,
three different regular enemies instead of the first three Hollows, and a Taurus Demon in place of the Asylum Demon. Each shows its exact slot, the distance from the start, the expected
replacement, an estimated time (20-90 s) and specific checks. **No override or forced placement was needed:** the quick worlds are ordinary worlds whose master seeds were *found* by
`apps/backend/scripts/smokeWorldSearch.mjs` (engine only, real catalog) from normal settings; they stay normal Seed Codes, so nothing in generation, Seed Codes or history changed.
`smokeTestCatalog.mjs verify` generates each quick world twice through the normal pipeline (identical package) and `tools/smoke-verify` reads the package: the MSB part has the promised
model, the model is loaded by the map, the NpcParam and NpcThinkParam rows exist, souls and the respawn bit are as promised, a wake-up event was added for the entity, event ids are
unique and the model count is within the limit. That proves the files contain the target; nothing was played. Details: `docs/SMOKE_TESTS.md`.
- **Found by the static check and fixed in backend 0.35.1 (see the entry above this one):** a roaming boss row is a copy of the boss row, and boss rows carry `disableRespawn = 1`; Part E1 only ever *set* the bit, so with "Roaming bosses stay dead" **off** it stayed 1.
- **Also seen, not changed:** the roaming pass treats dummy slots (`c2500_0004`, `c2500_0008` in the Asylum) as ordinary slots, so a boss can be placed on a part that is never spawned.

**Enemy placement Part E1: bosses in regular enemy slots (2026-10-06; backend 0.34.0, desktop 0.21.0, contracts 0.19.0, patch 0014; RANDOMIZER_VERSION 0.5.66 and FogHost 0.5.0 unchanged; generation profile `ds1tr-gen-v1` unchanged).**
`sampling-v1` with Normal enemy replacement "Only bosses" or "Bosses or normal enemies" places bosses in regular enemy slots ("roaming bosses"), with a
"Boss chance in normal slots" (only with "either"), "Roaming boss souls" (percent of the authored fight souls, written to the scaled NpcParam row's `getSoul`) and
"Roaming bosses stay dead" (the row's own `disableRespawn` bit; **not yet confirmed in the game**). Type replacement "except roaming bosses" is now real. The
controls appear only when they apply and are put back to their defaults when they stop applying. 17 boss archetypes can roam; Bed of Chaos, Ceaseless Discharge,
Dark Sun Gwyndolin, Lord Gwyn and every multi-body boss never do; at most 10 roam in a map; the model limit never gets worse (no Custom Pool grace). **Part E2 (a
regular enemy in a boss slot) is not built** and shows no control; the reasons and what a safe version needs are in `docs/ENEMY_PLACEMENT_MODEL.md` 10.7. Part F (NPC /
hostile NPC replacement) is untouched. Details, legacy semantics, deviations and measurements: `docs/ENEMY_PLACEMENT_MODEL.md` 10.7.

- **Seed Codes:** the three options are extension keys (stored only when non-neutral); `normalReplacement` gained two values an older code cannot contain. All 15 earlier golden
  fixtures verify byte for byte with the Part E engine and data tool; three new fixtures pin roaming worlds (`ds1tr-gen-v1.roaming-bosses.json`). Presets are version 11.
- **Tooling:** `apps/backend/scripts/roamingSweep.mjs` (engine on a real catalog, invariants), `roamingProbe.mjs` (keeps one package), `placementMatrix.mjs roaming-e1`
  (real data tool), six Part E smoke worlds in Advanced (`docs/SMOKE_TESTS.md`).
- **Measurements:** engine 1800/1800 worlds (18 configurations x 100 seeds); real data tool 1780/1780 runs (including 300 baseline runs); Fog 120/120 with `verify-skips` clean, boss overlay unchanged (15 events), 0 conflicts; no map above its own model baseline (Undead Parish 33 as before); 15/15 earlier golden fixtures byte-identical; full table in `docs/ENEMY_PLACEMENT_MODEL.md` 10.7.
- **Not verified in the game (needs playtesting):** that a roaming boss wakes up, navigates, can be killed and drops the intended souls; that "stay dead" holds after resting at a bonfire;
  that no map shows missing textures or crashes with up to ten bosses and 30+ models; the Fog interplay at runtime. Use the Part E worlds under Advanced, Test worlds for playtesting.

**Enemy placement Part D: boss sampling (2026-10-06; backend 0.32.0, desktop 0.19.0, contracts 0.16.0, patch 0012; RANDOMIZER_VERSION 0.5.66 and FogHost 0.5.0 unchanged; generation profile `ds1tr-gen-v1` unchanged).**
`sampling-v1` can draw a replacement for every single-body boss slot from the boss pool (`Boss replacement: Random bosses`), with the
replacement chance per boss slot, the Gwyn nerf per candidate and an optional `Unique bosses` cycle; the controls replace two PLANNED
placeholders. Linked units (Priscilla, Kalameet, Gaping Dragon, Seath, Sanctuary Guardian, Bell Gargoyles, Centipede Demon) stay with their classic planners (unit participation is not built). Details,
evidence and open points: `docs/ENEMY_PLACEMENT_MODEL.md` 10.6. The two new options are **extension keys** of the Seed Code (stored only when
non-neutral), so no existing code changed and none can decode to another world (`docs/SEED_FORMAT.md`, `docs/DECISIONS.md`); presets are version 10.

- **Real format:** 100 boss-sampling seeds with Fog (40 chance 100, 40 Unique at chance 60, 20 with Enemy scaling `vanilla`): 100/100 success,
  boss overlay set unchanged (the same 15 events), 12 instruction merges as before, 0 conflicts, `verify-skips` clean on every package, maximum 30
  models per map; 40/40 for the classic reference. A first run failed 36/40 (`Conflicting custom NpcThinkParam definition`); fixed (per-slot ids).
- **Compatibility:** 24 permutation lock hashes and the sampling pins identical; the 10 original golden fixtures, the two sampling-v1 + classic-boss baselines
  recorded before this change and the two new boss-sampling fixtures reproduce from their Seed Codes (also with a simulated future app version).
- **Found, not fixed:** `sampling-v1` with Type replacement `enabled` and Placement fit `anywhere` fails for some seeds in `patch-enemies`
  ("Randomizer event ID is already used: 11419980", a Bed of Chaos floor event registered twice); present before this change.
- **Tests:** new boss sampler tests, Seed Code extension-key tests, preset v10 migration.

**State-based run area, branding, Fog wording (2026-10-06; desktop 0.18.0, backend 0.31.0, contracts 0.13.0; generation profile ds1tr-gen-v1 unchanged).**
The run area now follows the state of the game (`deriveRunPhase`): original game shows Randomize and Load seed; a generated world shows
Apply to game with Copy seed secondary; an applied game shows Launch game with Restore original game secondary; an interrupted change
shows only Restore. One current message at a time (old success banners are cleared when the next action starts). Load seed generates
the shared world immediately (no "Generate this world" step); when the game is not original, restoring is the one thing asked, and the
world is created automatically afterwards. Run history is collapsed by default (`Run history  N  v`); a row shows time, status, a
shortened Seed Code and Items/Enemies/Bosses/Fog chips with Apply / Play (or Launch game), Copy seed and an overflow menu (View
settings, View spoiler log, Regenerate files, Delete from history). **Regenerate files** (formerly Recreate) refreshes the existing
history entry (same id, Seed Code and creation time) instead of adding one; so does loading a Seed Code that is already in the history.
The user-facing wording is Fog again ("Fog randomizer settings"); the master seed stays under Advanced. The packaged app has no
File/Edit/View/Window/Help menu bar. Branding: the approved icon (`apps/desktop/assets/branding/ds1tr-icon-source.png`) is converted by
`apps/desktop/scripts/make-icon.ps1` into `apps/desktop/build/icon.ico` (16, 24, 32, 48, 64, 128, 256 px) and used by electron-builder
and the window; a small decorative brand mark sat at the right of the header (withdrawn in 0.35.0; the interface has none now).

- **Packaged proof (0.18.0):** menu bar absent (window client area equals window minus frame, `GetMenu` = 0); window, taskbar and EXE
  icons are the DS1TR icon; the whole flow Randomize, Apply, Restore, restart (settings and history persist), Load seed (auto
  generation, same Seed Code, settings restored, history still 1 entry), Regenerate files (same entry), Load seed while applied
  (restore-and-continue) was driven through the UI.
- **Tests:** backend 360 (358 pass, 2 skipped), desktop 5; new tests for the phase logic, history refresh/delete and the UX contract.

**Seed Codes, generation profiles, history and launch (2026-10-06; backend 0.30.0, desktop 0.17.0, contracts 0.13.0; RANDOMIZER_VERSION 0.5.66 and FogHost 0.5.0 unchanged).**
A world is now named by a self-contained Seed Code that carries an immutable generation profile (`ds1tr-gen-v1`), the internal
master seed and the complete canonical settings; the app version is not an input of generation. New: Randomize / Load seed (the
master seed moved to Advanced), persistent settings and run history (Apply, Regenerate files without the run folder, Copy seed, View
settings), explicit Apply / Restore success messages and a Launch game button (Steam, executable fallback). Formats and rules:
`docs/SEED_FORMAT.md`, `docs/GENERATION_COMPATIBILITY.md`, `docs/DECISIONS.md`.

- **Golden fixtures:** `apps/backend/fixtures/golden/ds1tr-gen-v1.json`, 10 permanent cases recorded from a clean game copy (items;
  enemies classic; sampling-v1; items+enemies+bosses; +Fog; broad items with shops; custom pool; second Gargoyle; one-model
  grace; world options). Verified from the Seed Codes alone, also with a simulated application version (`99.1.0-future`):
  identical profile, settings, master seed and package hashes, 10/10.
- **Engines untouched:** no change in `engines/`, seed derivation or retry bounds; only the profile plumbing and drift guard.
- **Tests:** backend 354 (352 pass, 2 skipped: the pre-existing one and the golden regeneration that needs a clean game copy);
  new `seedCode`, `seedPersistence`, `generationProfiles`, `goldenCompatibility`, `seedUx`; desktop 5 (settings migration, launch game).

**World randomizer item tracking (2026-10-06; backend 0.29.1, desktop 0.16.1, FogHost 0.5.0, RANDOMIZER_VERSION 0.5.66).**
Fixed a real failure of the packaged app: with more item categories on, Fog stopped with `Couldn't find darkmoon_seance_ring in item
lots` after three identical combined attempts and the app said "try another seed". FogMod needs exactly one copy of each
required key item in the item lots it annotates and at most one of any; the global item shuffle put the Seance Ring and the
Peculiar Doll outside them (8% to 93% of the seeds depending on the categories) and put the shop copies of the Crest of
Artorias, Residence Key and Cast Light inside them. The shuffle now gives every source of a tracked key item a role (the one
copy: annotated lots only; extra copies: shops only) read from the verified `fog.txt`; nothing is duplicated and no item
category is blocked. `FOG_UPSTREAM_INCOMPATIBLE` is structural (no combined reseeding) with a plain message. Details and
evidence: `docs/FOG_ITEM_TRACKING.md`. Patch `0011`.

- **Real format:** 40 seeds, all item categories, bosses on, Fog on: 40/40 on the first attempt, 0 merge conflicts, same 15 overlay
  events, `verify-skips` clean, deterministic rerun, activation and restore; packaged 0.16.1 through its own UI: Randomize, Apply,
  Restore succeed.
- **Tests:** backend 327 (326 pass, 1 skipped); new `fogTrackedItems.test.mjs` (13) and pipeline tests.
- **Test harness note:** the Fog end-to-end script reconstructs the game from the live installation. Once the app has applied a run to
  the live game that copy is no longer clean (duplicate events in the boss-name patch), so the script can now take a clean copy
  (`DS1TR_E2E_GAME_SOURCE`, for example the app's pristine snapshot).

**User-friendly runtime / Fog bootstrap (2026-10-05; backend 0.29.0, desktop 0.16.0, contracts 0.12.0, FogHost 0.5.0).**
A player no longer picks a FogMod folder or installs .NET. The app finds Dark Souls Remastered through Steam, checks it,
imports the clean snapshot and prepares the world randomizer data itself: 19 of the 59 files are copied from the verified
clean snapshot, 40 are fetched once over HTTPS from the pinned FogMod commit (SHA-256 per file, size bound, temp folder,
whole-set verification, atomic install), after which no network is needed. FogHost and DsrDataTool are self-contained
win-x64 single-file executables. The main screen uses plain wording and readiness indicators; failures are phrased for
players and the exact reason stays in the log and manifest. Details: `docs/FOG_DATA_PROVENANCE.md`,
`docs/DESKTOP_PACKAGING.md`, `docs/DECISIONS.md`.

- **Proof through the packaged path (no `DS1TR_FOG_DATA_ROOT`):** a full Items + Enemies + Bosses + Fog proof with the
  self-contained helpers and an empty state folder (first-run download): generation, byte-identical rerun, activation,
  restore, live installation unchanged. The packaged 0.16.0 app, driven through its own UI on a clean copy of the game: game
  folder check, world data preparation, Randomize, Apply to game, Restore original game.
- **Fixed on the way:** the 0.15.0 portable executable could not start its backend (`@ds1tr/contracts` was not staged).
- **Tests:** backend 312 (311 pass, 1 skipped; new: world data bootstrap, game discovery, prerequisites, plain messages),
  desktop settings migration 2.

**Enemy placement Part C2: custom enemy pool for regular enemies (2026-10-05; contracts 0.12.0, backend 0.28.0, desktop 0.15.0).**
`sampling-v1` gains the custom enemy pool (**PARTIAL**: regular enemies; the original's boss and NPC entries need later
parts). A pool is a canonical sorted list of model ids (`null` = the full pool); it enters only through the candidate
boundary, restricts replacements only (unselected enemies stay where they are, including as the vanilla fallback) and
changes none of fit, Prevent same, Easy Asylum, Type replacement, drop ownership or determinism. Presets are version 9
(1-8 migrate to `permutation-v1` with the full pool, a stale id is an error). The backend lists the regular enemies from the
clean game data (`POST /api/enemy-pool`, cached per snapshot) and the desktop gets a filterable editor. Details: model
document 10.5. Patch `0010`.

- **Model budget (decided 2026-10-05):** nominal 30; the baseline rule `max(30, baseline)` is unchanged; a restricted pool may
  use a **one-model grace to 31** only on a map whose base cap is 30, when 30 is proven impossible and 31 proven sufficient.
  A baseline already above 30 is never raised and nothing above 31 is ever granted. Recorded per map (`nominalCap`,
  `minimumFeasibleModels`, `customPoolModelGrace`). 31 is not a new general limit.
- **Accounting fixes found by the 100-seed validation:** the pool never reached the coverage reserve; the reserve could pick a
  model too big for the smallest slot; the Type ledger could not serve the first group of a map that starts above its cap.
  A coverage witness (the fewest models that keep every participant servable) replaced the reserve of restricted pools;
  slots no pool model can replace join the baseline. The retry bound (3) was not changed.
- **Errors:** structural problems and, at replacement chance 100%, a map whose proven-minimal cover cannot fit even with the
  grace (`map-infeasible`) raise the non-retryable `ENEMY_PLACEMENT_CUSTOM_POOL_INVALID`; a participation draw that does not fit is the
  ordinary `ENEMY_PLACEMENT_INFEASIBLE` (at most 3 map attempts). No percentage or size heuristic rejects pools.
- **Catalog sweep (100 seeds x 18 maps, many pools):** A, B, C, E, N 100/100 in every tested configuration; D at "anywhere"
  100/100, D at "fits" 100% refused or served exactly where provable; 0 fit / Prevent-same / pool-membership / cap violations;
  the grace was used only on Parish and Lost Izalith and ended at 31 models; no baseline above 30 was increased.
- **Real format (Items + Enemies + Fog + bosses):** 50 seeds in 5 pool configurations + 3 full proofs: all pass, 0 merge
  conflicts, same 15 overlay events, `verify-skips` 0 failures, live installation unchanged.
- **Proofs:** `permutation-v1` unchanged (24 hashes, package files of combined-only and combined + Fog runs); the full pool
  equals Part C1 (synthetic pins and 700 real-catalog outputs, same `classTableHash` and retry counts).
- **Parish risk unchanged** (32-33-model baselines of 10.3 are separate and not worsened).
- **Tests:** backend 282 (281 pass, 1 skipped); new `enemyCustomPool.test.mjs` (28 tests); typecheck and desktop build pass.

**Enemy placement Part C1: Type replacement for regular enemies (2026-10-05; contracts 0.11.0, backend 0.27.0, desktop 0.14.0).**
`sampling-v1` gains the legacy Type replacement (**FULL** for regular enemies: disabled = legacy default, enabled, enabled
except roaming bosses = enabled until boss sources exist). The group is the original model within a map; it is decided once
(replacement chance, difficulty class, candidate) from its own stream `type:<map>:<model>` (attempt-aware), every member
slot gets the same replacement and draws only its variant; the candidate fits every member and differs from the
other groups of the map. The model budget is now reference counted for groups (`ModelLedger`: a replaced group frees its
original model). Presets are version 8 (1-7 migrate to `permutation-v1` with Type replacement disabled). Details: model
document 10.4. Patch `0009`. Custom pool (C2) is not started; the candidate pool is an injectable boundary.

- **Measured conflict and decision inside the task:** with strict legacy injectivity 93 of 100 seeds failed at 100% chance
  (the Parish: 15-16 fixed + 17 groups = 32 models, cap 30, deterministic, retries cannot help). Injectivity is now the last
  rung to yield (`shared model`, about 1.6 groups per seed at 100%, 0.01 at 60%, 0 at 25%); fit, budget and fixed entities
  are never relaxed. The retry budget (3) was not changed.
- **Proofs:** `permutation-v1` unchanged (24 hashes; 56/56 and 73/73 package files); with Type replacement off the output
  equals Part B (32 synthetic pins, and 700 real-catalog outputs with identical `classTableHash` and retry counts).
- **Catalog sweep:** 10 variants x 100 seeds, 0 final failures, 0 fit / Prevent-same / cap violations, retry pressure
  at or below Part B. **Real format:** 50 seeds (Items + Enemies + Fog + bosses) + 3 full proofs: all pass, 0 merge
  conflicts, same 15 overlay events, `verify-skips` 0 failures.
- **Parish risk unchanged** (not solved here): with Type replacement the Parish ends at 28-32 models; seeds `parish-26` (31),
  `parish-1` (32), `parish-11`/`parish-34` (map retry) in 10.4.
- **Tests:** backend 253 (252 pass, 1 skipped); new `enemyTypeReplacement.test.mjs` (24 tests); typecheck and desktop build pass.

**Enemy placement Part B: difficulty and map-local retries for regular enemies (2026-10-05; contracts 0.10.0, backend 0.26.0, desktop 0.13.0).**
`sampling-v1` gains Difficulty mode (curve, fully random, and both easy-Asylum modes: legacy default random +
easy Asylum) and Difficulty strictness (strict, a bit loose, very loose), both **FULL** for regular enemies. Classes are
derived by DS1TR (`regular-class-v2`: map progression tier and HP rank, in `classTableHash`), not copied; strictness
tables and the Easy Asylum cap are authored. A map that cannot be placed is replanned alone with attempt streams
(`attempt:<n>:slot:<id>`, at most 3 attempts; attempt 0 equals Part A), so the former whole-run retry for
`ENEMY_PLACEMENT_INFEASIBLE` is now almost never needed. Presets are version 7 (1-6 migrate to `permutation-v1` with
neutral difficulty). Details, eligibility matrix, filtering order and results: model document 10.3. Patch `0008`.

- **Proofs:** `permutation-v1` unchanged (24 hashes; 56/56 and 73/73 package files); 24 Part A synthetic hashes
  identical; real catalog: of 700 Part A outputs 681 are identical and the other 19 (the Part A first-attempt failures) are now solved by a map retry; none differs.
- **Catalog sweep:** 13 variants x 100 seeds: 0 final failures, 0 sampler overflows, 0 fit / Prevent-same / Easy
  Asylum violations. **Real format:** 50 seeds (Items + Enemies + Fog + bosses) + 3 full proofs incl. retried Parish
  maps: all pass, 0 merge conflicts, same 15 overlay events, `verify-skips` 0 failures.
- **Parish risk unchanged:** 31-33 models are reproducible with `parish-12` (31), `parish-11` (32), `parish-28`
  and `parish-92` (33) (config in 10.3); not tested in the game.
- **Tests:** backend 228 (227 pass, 1 skipped); new `enemyDifficulty.test.mjs` (18 tests); typecheck and desktop build pass.

**Enemy placement Part A: `sampling-v1` for regular enemy slots (2026-10-05; contracts 0.9.0, backend 0.25.0, desktop 0.12.0).**
`sampling-v1` is implemented and opt-in (UI: "Legacy-style sampling"); `permutation-v1` stays the default and its
output is unchanged (24 placement hashes; 56/56 and 73/73 package files). It samples regular enemy slots only;
bosses, units and NPCs are planned as before. Controls: Replacement chance (**FULL**, 0-100), Normal enemy
replacement (`none`, `normals`: PARTIAL), Placement fit (**FULL** for regular slots, 0 violations), Prevent same
(FULL). Presets are version 6 (1-5 migrate to `permutation-v1` with neutral settings). Engine:
`vendor/src/core/sampling.js` (patches `0006`, `0007`), class table `regular-size-v1` with `classTableHash`,
coverage reserve, fallback ladder, per-slot streams, slot-owned drops, summary/detail diagnostics.

- **Model budget (decided): non-worsening.** sampling-v1 uses a non-worsening model-budget policy. The normal target is 30 distinct models per map. If out-of-scope/fixed plans already require more than 30, that unavoidable baseline becomes the effective cap. The regular sampler may preserve or reduce that overflow but may not increase it. Details: model document 10.2, `docs/DECISIONS.md`.
- **Catalog validation:** 100 seeds x 17 variants (0/25/60/90/100% x fits/anywhere/Prevent same): 0 sampler
  overflows, 0 fit violations, 0 Prevent-same violations, deterministic. With `fits` at 25%/60% a first attempt
  fails in about 9%/5% of seeds with the retryable `ENEMY_PLACEMENT_INFEASIBLE` (zero headroom in the Parish).
- **Real-format validation:** 55 seeds (100%, 60%, 25%, Prevent same, mode `none`) through the full pipeline with
  Fog and bosses: 55/55 generated, 0 merge conflicts, boss overlay unchanged (15 events), `verify-skips` 0
  failures, byte-identical reruns, live install untouched.
- **Known risk:** the Parish can end at 32-33 models (baseline) when vanilla regular models are kept; not tested in-game.
- **Tests:** backend 210 (209 pass, 1 skipped); `enemySampling.test.mjs` (34 tests), preset v1-v6, mapper,
  bridge and pipeline classification tests; root typecheck and desktop build pass.

**Enemy placement Part 0: infrastructure only (2026-10-05; backend 0.23.0, contracts 0.8.0, desktop 0.11.0).**
Prepares the contract, backend, engine and tests for the future `sampling-v1` placement algorithm without
changing any placement. `permutation-v1` remains the default and the only algorithm that runs; the sampler is
**not implemented**, and selecting `sampling-v1` is refused with `ENEMY_PLACEMENT_ALGORITHM_UNAVAILABLE` at
validation, mapper, bridge and engine level (never a silent fallback). Presets are version 5
(`enemies.placement.algorithm`); versions 1 to 4 migrate to `permutation-v1`. Engine: new
`vendor/src/core/placement.js` (patch `0005`, listed in `UPSTREAM_PIN.md` and the module-set test), the config
key `enemyPlacementAlgorithm`, the dispatch `planExtractedEnemies`, per-slot RNG substreams
(`enemy-placement-v1`, `slot:<id>`), `ModelBudget` (cap 30, no per-map exception), diagnostics and
drop-ownership helpers. Model document: section 10.1.

- **Unchanged output (proof):** 24 placement hashes identical; two combined-only packages identical in all 56
  non-metadata files and the combined + Fog package identical in all 73 (metadata files embed the temporary
  path and gain only the new fields). `RANDOMIZER_VERSION` 0.5.66 and FogHost 0.5.0 are untouched; the
  portable EXE was not rebuilt.
- **Model budget:** checked on the real catalog; vanilla maximum 28 distinct models, none above 30, so the
  safety-first cap of 30 rejects no vanilla map.
- **Known and unchanged:** `permutation-v1` with Enemy scaling `vanilla` still duplicates single-owner
  equipment drops (model document 9.1); `sampling-v1` must always create a scaled row.
- **Tests:** backend 168 (167 pass, 1 skipped), including the new `enemyPlacementInfrastructure.test.mjs`
  (gating, RNG substreams, budget, diagnostics, drop ownership, permutation untouched), preset v1-v5 migration,
  mapper and bridge gating; root typecheck and desktop build pass.

**Enemy placement design (2026-10-05).** `docs/ENEMY_PLACEMENT_MODEL.md` reconstructs the legacy Enemy
Randomizer's per-slot placement model, documents the current permutation engine, compares four
architectures and fixes the target: a new `sampling-v1` planner beside the frozen `permutation-v1`
(`docs/DECISIONS.md`), with an incremental plan (batches 0, A-G) for Replacement chance, the replacement
modes, Boss chance, Placement fit, Difficulty mode and strictness, Type replacement, and the options that
build on them. **No runtime behavior, default, preset, FogHost code or packaging changed.**

- **Prototype** (`engines/combined/prototypes/enemy-placement-sampler.mjs`, scratch, not packaged: the packager
  copies only `engines/combined/vendor/src`): regular-enemy sampler on the real catalog, 1,703 slots, 40 seeds
  per variant, every run repeated for determinism. With a coverage reserve: 0 fit violations, 0 budget errors,
  0 non-deterministic runs, 6-8 ms per seed. Without it: 0.13% of placements end above their size class.
  Curve adherence inside a full model set is 30-31% under the literal legacy rule and 72-77% with the
  documented refinement.
- **Finding (current engine, not fixed):** with Enemy scaling `vanilla` and drops not randomized,
  `permutation-v1` duplicates single-owner equipment drops in 95 of 100 seeds (92 from the Titanite Demon
  group's Titanite Catch Pole; the rest from archetype-duplicated bosses: Pinwheel's masks, Taurus's axe,
  Super Ornstein's ring, Capra's weapon), because placements without a scaled row keep the replacement's own
  drops. Gwyn nerf and Second Gargoyle add about 0.05-0.10 lots per seed. The default `area` scaling shows none
  (0 of 100). None of FogMod's 27 tracked items is reachable through an NpcParam drop lot, so there is no Fog
  or progression impact. The new design makes "drops follow the slot" a rule (model document 6.9).
- **Derived classes** versus the legacy ones as a yardstick: size rank correlation 0.74 (79 of 82 within one
  class), difficulty 0.58 (73 of 82 within two).
- **Open product questions** are listed in section 13 of the model document (default algorithm, model budget
  definition, difficulty overrides, boss scaling, NPC allowlist).
- **Verification:** unchanged runtime, so no new runtime tests were needed; the prototype is not part of
  `npm test`. The earlier backend (152), FogHost (74), typecheck and desktop-build results stand.

**Replace second Gargoyle (2026-10-05; backend 0.22.0, contracts 0.7.0, desktop 0.10.0).**
The option now follows the legacy Enemy Randomizer: legacy default Replace; the second Bell Gargoyle
(`m10_01_00_00:c5350_0001`) is replaced by an independently drawn boss, or left alone with Do not
replace. A third value, Single boss, is the earlier DS1TR behavior and exists only so older presets
reproduce their output. Semantics: `docs/FEATURE_PARITY.md` 6a; policy: `docs/DECISIONS.md`;
patch `engines/combined/vendor/patches/0004-legacy-parity-second-gargoyle.patch`. Presets are version 4
(versions 1 to 3 migrate to Single boss). Parity is now 29 FULL of 54.

- **No FogHost change (FogHost stays 0.5.0).** The Batch 1 stop was based on a wrong premise: FogMod makes
  no edit of its own to events `11015396` and `11015382` (it copies the game directory's instructions
  into them), so the normal merge passes the combined forms through. The 15-event boss overlay is
  unchanged. Instruction-level comparison: `docs/FOGHOST_INTEGRATION.md`, "Second Gargoyle events".
- **FogHost tests:** 74/74 (68 existing + 6 new), run with a manually compiled test assembly because no
  .NET SDK is installed: pass-through of both events without the overlay, hold / 60% / release order,
  exact 15-event overlay set, five divergent FogMod forms refused (added control flow, missing anchor,
  duplicate anchor, added parameter, changed threshold), vanilla forms merge to nothing, and a real FogMod
  run writes both legacy forms out unchanged.
- **Engine checks (real catalog, in-process):** over 40 seeds Replace drew 16 distinct bosses, was
  deterministic, produced no duplicate slot, entity or scaled-NpcParam ids, kept the Parish model count
  at most 30, never drew a Bell Gargoyle, linked unit, excluded Gwyn or (with Chaos on) Pinwheel, left the
  third body and second tail disabled, and added no forced combat activation. Keep produced no placement for
  the body or its tail. Single boss (the engine default) leaves the 24 baseline placement hashes identical.
- **Real `patch-enemies` / `verify-skips`:** Replace, Keep and Single boss accepted, 0 skip mismatches.
  Event `11015396` with Replace holds the body (AI off, invincible), waits for 1010800 at 60%, releases
  it and renames the boss bar; with Keep it is byte-identical to vanilla.
- **Proof run** (generate, byte-identical rerun, activate, restore): all pass, and events `11015396`
  and `11015382` in the final Fog package are identical to the combined package's. Live install untouched.
- **Fog sweeps** (Items + Enemies + Bosses + Fog, live install unchanged, `verify-skips` on every attempt):

  | Option | Seeds | Success | Merge conflicts | Key-item retries | Skip checks | Mismatches |
  |---|---|---|---|---|---|---|
  | Replace | 20 | 20 | 0 | 0 of 20 seeds | 20 | 0 |
  | Do not replace | 8 | 8 | 0 | 2 of 8 seeds | 10 | 0 |
  | Single boss (regression) | 8 | 8 | 0 | 0 of 8 seeds | 8 | 0 |

  Boss overlays stayed on the 15 known events; neither Gargoyle event appears under merges or overlays.
- **Tests:** backend 152 passed, 0 failed, 1 skipped (needs a user Fog data root); root typecheck clean;
  desktop build passed. The portable EXE was not rebuilt.
- **Residual risk:** whether each portable boss's AI wakes correctly from the vanilla release (`set_ai(on)`,
  plus the combined engine's usual wake-up rows) cannot be checked without running the game.

**Legacy parity Batch 2 (2026-10-05; backend 0.21.0, contracts 0.6.0, desktop 0.9.0).**
Soul items (Shuffled and Replaced), Starting items, Pinwheel Chaos and Gwyn nerf are functional end
to end with their legacy defaults (soul items shuffled, starting items Shield + 1H/2H, Pinwheel Chaos
off, Gwyn nerf Medium). Semantics, deviations and legacy references: `docs/FEATURE_PARITY.md` 6b;
policy: `docs/DECISIONS.md`; patch: `engines/combined/vendor/patches/0003-legacy-parity-batch-2.patch`.
Presets are now version 3 (versions 1 and 2 migrate to the behavior-preserving values).

- **Not implemented:** Soul items *Transposed* (boss souls are in none of the catalog's pools; shown
  as a disabled choice), Replace second Gargoyle (still blocked, see Batch 1) and the hintsheet.
- **Include DLC was a dead control** (found by the trace: no backend mapping, no engine reader, no
  FogMod switch). The backend now rejects `includeDlc: false`; the UI shows it as a disabled planned
  control. A real implementation is medium work and cannot cover Fog.
- **A bug the Fog sweep caught:** a first version of the Replaced-souls pool included the unique
  Peculiar Doll, producing `Item peculiar_doll found in both ...` (FogMod tracks 27 key items by id)
  in 10 of 10 seeds. The pool now only holds goods that occur in at least two lots; none of the
  pool's 31 goods is among FogMod's key items.
- **Engine defaults unchanged:** the 24 Batch 1 placement hashes are still identical.
- **Real-catalog checks (in-process):** Replaced souls removed all 3380 lesser souls over the runs and
  kept Fire Keeper Souls; shield starting modes gave shields in every class with unique pickups and
  no assignment failures over 180 seeds per mode; Gwyn appears in about 18% (strong) and 40%
  (medium) of 100 seeds; Pinwheel Chaos replaced exactly the 14 clones with the Catacombs model count
  at most 30 and the main Pinwheel untouched.
- **Real `patch-enemies` / `verify-skips`** (live install, scratch output): all options accepted,
  0 skip mismatches. Pinwheel Chaos edits fewer events in `m13_00` than the default
  (`11305392/396/398/250` are no longer edited); `11305100` is never edited.
- **Fog sweeps** (live install unchanged): product defaults 20/20 (0 conflicts, 21 skip checks, 0
  mismatches, 1 retry in 21 attempts); every Batch 1 and Batch 2 option non-default 12/12 (0
  conflicts, 15 skip checks, 0 mismatches, 2 seeds needed a combined retry). Boss overlays stayed on
  the 15 whitelisted events.
- **Tests:** backend 144 passed, 0 failed, 1 skipped (needs a user Fog data root); new engine tests
  cover every option on synthetic catalogs; root typecheck clean; desktop build passed. The portable
  EXE was not rebuilt.
- **Remaining:** see the gaps in `docs/FEATURE_PARITY.md` section 6.

**Legacy parity Batch 1 (2026-10-05; backend 0.20.0, contracts 0.5.0, desktop 0.8.0).**
Four legacy options are now functional end to end, each with its original legacy default:
Fashion Souls (on), Replace mimics (on), Replace respawning mosquitoes (on) and Prevent same
enemy (off). The vendored core was extended (`patches/0002-legacy-parity-batch-1.patch`);
semantics, deviations and the legacy source references are in `docs/FEATURE_PARITY.md`
section 6a. Presets are now version 2; version 1 presets migrate with the legacy defaults.

- **Not implemented:** Replace second Gargoyle (stop condition: it would edit events
  `11015382` and `11015396`, which FogMod rewrites and which are outside the 15-event boss
  overlay whitelist) and the hintsheet (it reports shuffled key locations, which DS1TR does
  not produce). Both remain documented as UI-only / partial. The portable EXE was not rebuilt.
- **Default output unchanged:** 24 placement hashes (three configurations x eight seeds) from
  the real-format catalog are identical before and after the change.
- **Real-catalog checks (in-process, 60 seeds):** prevent-same left 0 same-model regular
  placements (about 47 per seed before) with at most 30 models per map and deterministic
  output; excluded mimic and mosquito slots were never placed; with Fashion Souls off, all 31
  regular armor homes held a complete, distinct vanilla set in each of 40 seeds.
- **Targeted Bosses + Fog sweep** (5 seeds, mimics off, mosquitoes off, prevent-same on,
  Fashion Souls off): 5/5 succeeded; 0 merge conflicts; boss overlays applied only to the 15
  whitelisted events; 9 combined-package skip checks, 0 mismatches; no Fog/combined event
  mismatches; live installation unchanged. 3 of 5 seeds needed a combined key-item retry
  (4 of 9 attempts), within the range recorded earlier (10 of 50); none exhausted the budget.
  No full 40-seed sweep was run because defaults are unchanged.
- **Tests:** backend 124 passed, 0 failed, 1 skipped (needs a user Fog data root); new
  `combinedLegacyParity.test.mjs` covers the engine on synthetic catalogs (no game data is
  committed); root typecheck clean; desktop build passed. Two runs immediately after the
  sweep hit the integration test's 10 s server-start timeout; four later runs were clean.
- **Remaining:** see the engine gaps in `docs/FEATURE_PARITY.md` section 6; the next batch
  should be the A-B items (Soul items, Starting items, Pinwheel Chaos, Gwyn nerf).

**Feature-parity audit and settings presets (2026-10-05).** `docs/FEATURE_PARITY.md` audits
54 original options (Item 11, Enemy 27, FogMod 16) plus 16 combined-engine native options.
FogMod parity is complete. 21 original options are FULL, 30 are UI-only placeholders that
need new engine behavior, 1 is partial (hintsheet), 1 is intentionally unsupported (PTDE)
and 1 is a duplicate (copy enemy placement). The vendored combined engine reads none of the
legacy Item/Enemy options, so no `PLANNED EXTENSION` control became functional and none was
faked.

- **Newly wired:** settings preset import/export (`/api/preset/*`, backend 0.19.0, contracts
  0.4.0, desktop 0.7.0) and a "Restore default settings" button.
- **Verification:** root typecheck clean; backend tests 110 passed, 1 skipped (needs a user
  Fog data root); desktop build passed. No engine or real-data sweeps were run because no
  engine behavior changed.
- **Remaining:** engine gaps G1-G23 in `docs/FEATURE_PARITY.md`, classed A/B/C by effort and
  Fog risk. The portable executable has not been rebuilt for desktop 0.7.0.

**Windows desktop packaging (2026-09-29):** Electron now starts and stops the backend,
resolves bundled runtime files, stores mutable data outside the app directory, and
offers a FogMod data-root picker. Desktop 0.6.1 and backend 0.18.0. A portable x64
artifact is built at `apps/desktop/release/DS1-Triple-Randomizer-0.6.1-x64.exe`.
It bundles DsrDataTool, FogHost, the combined JS core and license notices; FogMod's
game-derived DS1R data is excluded. See `docs/DESKTOP_PACKAGING.md` for the layout,
build command and .NET 8 runtime prerequisite. Packaged-runtime API/tool smoke checks
passed on staging data. The final portable executable launched outside the restricted
sandbox: the renderer showed `Backend online` and first-run DSR/Fog guidance; health
returned 200 with backend 0.18.0; a missing DSR path returned an explicit problem;
the plan reported the combined engine and FogHost ready. Closing the window stopped
the portable wrapper, Electron main process and backend child. The sandboxed preload
had to compile as CommonJS (`preload.cts`); desktop 0.6.1 includes that fix. No public
GPL fork or release tag exists yet.

- Verification: root typecheck clean; backend tests 108 passed, 1 skipped (requires a
  user Fog data root); desktop, backend, DsrDataTool and FogHost builds passed;
  `package:win` completed; production-dependency audit found 0 advisories.
- Portable smoke: 0.6.1 executable launched from `release/`, renderer preload
  connected, backend health returned 200, missing DSR returned
  `DarkSoulsRemastered.exe not found.`, both packaged tools were reported ready,
  first-run DSR/Fog guidance was visible, and normal window close ended the wrapper,
  Electron main and backend child. Bundled DsrDataTool and FogHost executables ran
  and printed their usage messages. A staged-data run without a Fog root returned
  `FOG_PREREQUISITES_MISSING` before randomization. No live game files were touched.
- Release prerequisites: install .NET 8 runtime on the user machine; supply FogMod
  DS1R data locally; create the public GPL corresponding-source fork and matching
  release tag. Code signing and a custom icon are not configured.


**Boss randomization + Fog works (FogHost 0.5.0, backend 0.17.0, desktop 0.5.0,
2026-09-29).** The `FOG_BOSS_RANDOMIZATION_UNSUPPORTED` rejection is removed, and bosses
are on by default again.

- **Mechanism.** A whitelisted boss-event overlay: FogMod's event is the control-flow base,
  and the combined engine's known boss mutations are re-applied by content. It covers the
  15 boss events both engines edit. Details: `docs/FOGHOST_INTEGRATION.md`,
  `docs/DECISIONS.md`.
- **Also:**
  - FogMod patch 8 (Asylum Demon removal keeps skips consistent);
  - merge refinements: unambiguous-result alignments, skip range = covered lines, and
    event-0 initialization removal;
  - refused events log their base, combined and FogMod versions.
- **Verification:**
  - FogHost 68/68;
  - backend 109/109;
  - typecheck clean;
  - Items + Enemies + Bosses + Fog 40/40 (no conflicts, invariants pass, all skip checks
    clean);
  - 3 full proofs with byte-identical rerun and activation/restore;
  - regressions: Items + Enemies + Fog 5/5 and Items + Fog 5/5.
- **Remaining:**
  - the key-item combined-retry rate with bosses + Fog is 10 of 50 attempts, and no seed
    has exhausted the budget, so the budget is unchanged;
  - the overlay whitelist is fixed to the 15 measured events, so any other boss event
    edited by both engines stays a `FOG_MERGE_CONFLICT`;
  - the FogHost and DsrDataTool test consoles run outside `npm test`;
  - there is no public GPL fork yet;
  - desktop packaging has not started.

FogHost 0.3.1 (2026-09-29): FogMod's boss-trigger rewrite now keeps enclosing DS1 SKIP
counts consistent (vendored patch 7, `tools/fog-host/src/SkipRanges.cs`).

- **Verification:**
  - FogHost self-tests 52/52, including 5 new ones;
  - each of the 11 previously broken FogMod-only events (`11015393`, `11205383`,
    `11215002/3/12/13`, `11405393`, `11415343/383`, `11505393`, `11515393`) checked
    explicitly: 0 mismatches.
- **40-seed sweeps** (live install unchanged):
  - Items + Enemies + Fog 40/40;
  - Items + Fog 40/40.

  Neither had merge conflicts or skip mismatches, in combined packages or in final Fog
  packages (checked against FogMod's templates). Key-item retries are unchanged: 7 of 47
  and 8 of 48 attempts, and no seed exhausted the budget.
- **Remaining:**
  - bosses + Fog is still rejected;
  - the Asylum `11810310` FogMod removal is not skip-adjusted, but it runs only for
    bosses + Fog;
  - the FogHost and DsrDataTool test consoles run outside `npm test`;
  - there is no public GPL fork yet.

FogHost 0.3.0 and backend 0.16.2 (2026-09-29). **Items + Enemies + Fog works on real-format
data: 40/40 seeds.**

- **Changes:**
  - derived SKIP-count adjustments are accepted by the merge under strict conditions;
  - the Fog bridge now reads `randomizer.json` `placements` (the preserved-row list had
    been empty in production);
  - the model-declaration invariant false positive is fixed.
- **Verification:**
  - FogHost self-tests 47/47;
  - DsrDataTool tests 14/14;
  - backend 109/109 with the data root;
  - typecheck clean.
- **Sweep** (`fogHostE2e.mjs`, 40 seeds; live install unchanged):
  - 40/40 succeed, with post-write invariants passing;
  - 0 merge conflicts;
  - combined `verify-skips` 47/47;
  - final Fog packages have 0 skip mismatches in combined or merged events;
  - key items fell outside annotated lots on 7 of 47 attempts (5 seeds retried, none
    exhausted).
- **Remaining:**
  - 11 FogMod-only boss-trigger events have skip ranges broken by stock FogMod's own
    runtime rewrite. This is an upstream observation, not fixed here.
  - Bosses + Fog is still rejected.
  - The FogHost and DsrDataTool test consoles run outside `npm test`.
  - There is no public GPL fork yet.

DsrDataTool skip-count fix (2026-09-28; backend 0.16.1; user-authorized local vendored
modification). The combined engine now keeps DS1 "skip N lines" counts consistent when it
removes or inserts event instructions. Details: `docs/DECISIONS.md`.

- **Tests:**
  - DsrDataTool tests 14/14 (`engines/combined/tests/DsrDataTool.Tests`);
  - FogHost 37/37;
  - backend 108/108;
  - typecheck clean.
- **Real-format 40-seed sweeps**, with every combined package checked by `verify-skips`
  (live install unchanged):

  | Configuration | Result |
  |---|---|
  | Combined-only, Items + Enemies + Bosses | **40/40**; 40/40 packages with 0 skip mismatches (about 134 skips adjusted per package) |
  | Items + Fog | **40/40**; 48/48 skip checks pass; key-item retries on 8 of 48 attempts |
  | Items + Enemies + Fog | **0/40**; 47/47 skip checks pass (38 skips adjusted per package) |

  The Items + Enemies + Fog failures are FogHost merge refusals, not engine errors:
  - `11505392`: rule 4, because the combined engine now correctly changes a SKIP count
    (40 seeds);
  - `11600400`: the adjacency rule (39 seeds).
- **Open decision:** whether rule 4 may accept a skip-count change that is exactly
  explained by the same side's own edits inside that range.

FogHost 0.2.0 (2026-09-28): instruction-level EMEVD merge (user option B), a FogMod
scaling crash fix (patch 6), and boss randomization off by default on the desktop
(desktop 0.4.1).

- **Merge policy:** `docs/FOGHOST_INTEGRATION.md` rule 3 and `docs/DECISIONS.md`.
- **Verification:**
  - FogHost self-tests 37/37 (15 new merge tests, including the `11505392` pattern);
  - backend 108/108;
  - root typecheck clean.
- **Real-format sweeps** (`fogHostE2e.mjs`, `DS1TR_E2E_SWEEP=40`; live install
  unchanged):

  | Configuration | Result |
  |---|---|
  | Items + Fog | **40/40**. Key items fell outside annotated lots on 8 of 48 combined attempts (16.7%); 6 of 40 seeds needed a combined retry; none exhausted the budget. |
  | Items + regular enemies + Fog | **0/40**. 39 are `FOG_MERGE_CONFLICT`: `m15_00_00_00:11505392` in 39 seeds and `m16_00_00_00:11600400` in 38. `m15_00_00_00:11500001` merged at instruction level in 39 seeds. Key items fell outside annotated lots on 7 of 47 attempts (14.9%); 5 seeds needed a combined retry. One transient `DATA_TOOL_FAILED` did not reproduce on rerun. |

  Before patch 6, 11 of 40 enemy seeds crashed with `FOG_ENGINE_FAILED`.
- **Blocker:** both remaining conflicts are caused by the combined engine.
  `DsrDataTool.RemoveEventInstruction` deletes instructions inside SKIP ranges without
  adjusting the line count. One package had 38 such skips in 21 events, and this
  affects combined-only packages too. FogHost refuses to merge them by design. Fixing it
  needs a change to the vendored `DsrDataTool`, which is a user decision.

FogHost v1 (2026-09-28): `tools/fog-host` (FogHost 0.1.0, .NET 8) is built and verified;
`@ds1tr/backend` is 0.16.0.

- **Engine.** FogMod is re-pinned to `6e6dc78`, because `28711c3` cannot run DS1R. The
  host uses SoulsIds `e31a6fd` and `JuicerMV.SoulsFormats` 0.1.3. `GameDataWriter.cs`
  carries a documented patch set: API port, no backups, deferred writes, preserved
  NpcParam rows, and an EMEVD merge hook.
- **Host behavior:**
  - the schema-v2 contract is unchanged;
  - DS1R only, with `dryRun` and `write` modes, and writes only to staging;
  - deterministic output, SHA-256 output list;
  - three-way EMEVD merge;
  - in-memory and post-write invariants;
  - typed failures.
- **Data root.** The FogMod data root is supplied by the user (`DS1TR_FOG_DATA_ROOT`),
  never bundled. It is verified against a 59-file SHA-256 manifest.
- **Backend changes:**
  - a new default `getFogPrerequisites` that receives the Fog language (fixes the
    staged `msg/<language>` file);
  - boss randomization + Fog is rejected with `FOG_BOSS_RANDOMIZATION_UNSUPPORTED`.
- **Verification:**
  - FogHost self-tests: 22/22 with a data root, 12 passed + 10 skipped without one;
  - backend `npm test`: 108/108;
  - root typecheck clean;
  - real-format proof `apps/backend/scripts/fogHostE2e.mjs`:
    - Items + Fog passes 7/7 seeds, including Fog package activation and restore on
      a temp copy (live install unchanged);
    - **regular enemies + Fog fails 5/5**: four seeds hit `FOG_MERGE_CONFLICT` on
      `m15_00_00_00` event `11505392` (both engines edit it), and one seed exhausted
      the key-item retries.
- **Remaining:**
  - enemies + Fog needs an instruction-level merge or a product rule (user
    decision);
  - the desktop default (bosses on, Fog on) is now rejected by design;
  - the FogHost self-tests are not part of `npm test`;
  - no public GPL fork yet.

  Details: `docs/FOGHOST_INTEGRATION.md`, "FogHost v1 implementation".

FogHost preparation (2026-09-28, docs only, no code): the implementation
handoff is `docs/FOGHOST_IMPLEMENTATION_CHECKLIST.md`. It covers:
- the exact pins: FogMod `28711c3`, SoulsIds `e31a6fd` (Apache-2.0, verified),
  and the SoulsFormats candidates;
- the 60-file FogMod data root;
- the project layout, build commands and test plan;
- the `GameDataWriter.cs` patch points.

A throwaway build probe (outside the repo) showed that the audited FogMod
pin `28711c3` cannot generate DS1R (upstream DS3 regression). FogMod
`6e6dc78` + SoulsIds `e31a6fd` + NuGet `JuicerMV.SoulsFormats` 0.1.3, with a
six-rule API port of `GameDataWriter.cs`, builds on net8.0 and runs a
deterministic DS1R dry run and write. Source reading closed two open design
items. FogMod never removes enemy
parts. FogMod `scale` skips non-vanilla NpcParam rows and re-points
`NPCParamID` to clone rows, so the Task 11 "composed scaling" rule and
invariants 1 and 3 need a user decision before the host is built. No plan
task builds FogHost yet.

Task 16 is the final end-to-end audit. `@ds1tr/backend` is 0.15.1.

Fixed:
- **Pristine snapshot omitted `paramdef/`.** The snapshot is the only
  generation source, but `managedRoots` lacked `paramdef`. Without
  `paramdef/paramdef.paramdefbnd.dcx`, DsrDataTool `scan` silently produces
  a catalog with no gifts, drop lots, world lots, starting classes or enemy
  metadata. `patch-enemies` then fails reading it. Task 8 missed this because
  its hand-built copy included `paramdef/`. `paramdef` is now a managed root,
  and a gameState test pins the read-set roots. No snapshots existed yet, so
  no migration is needed.
- `npm test` failed when run from `apps/backend` (npm's cwd). The API
  integration test resolved `apps/backend` from cwd, and Node 24 crashed
  natively on the failed spawn. It now resolves relative to the test file.
- Manifest `appVersion` and `/api/health` report 0.15.1.

Verified (2026-09-28):
- Backend `npm test`: 103/103.
- `apps/backend/scripts/releaseAuditE2e.mjs` (not part of `npm test`; needs
  the live install, built DsrDataTool and .NET 8) passed 18/18. The live
  install was only read. A temp reconstruction of its managed roots (legacy
  `.bak` moved over originals, as in Task 8) went through the production
  path with a temp state root:
  - `importPristine`: 198 files, including paramdef;
  - `generateRun`: combined Items + Enemies with all item options, bosses and
    area scaling, Fog off, 54 patched files including GameParam;
  - a second run with the same seed is byte-identical in `mod/`;
  - no write into the game during generation;
  - `activate`: state `active`, and a second activation is refused;
  - `restore`: the game matches the reconstruction and the pristine store
    still verifies;
  - the live install is byte-identical afterwards.
- Manifest: records the master seed, per-attempt derived seeds and results,
  combined engine version, validation, file hashes, snapshot binding and
  appVersion.
- Licensing: no legacy Python engine code and no FogMod sources are
  tracked. The only vendored code is `kellernz1/ds-randomizer`
  (GPL-3.0-only) with notices. SoulsFormats (GPL-3.0-only, NuGet) is noted in
  both notice files. No game data is tracked.
- Retries are bounded and deterministic, and are recorded per attempt (unit
  tests). Combined retries are exercised only on `GENERATION_FAILED`.

Not verified / release blockers (not fixed here; outside a correctness fix):
- **Fog is not functional.** FogHost is unbuilt and no data root exists.
  No planned task builds it (the Task 12 prerequisite has no numbered task),
  so Task 17's FogHost availability check cannot pass until one is added.
  Fog-enabled generation always returns `FOG_PREREQUISITES_MISSING`. The
  combined -> Fog bridge, Fog retries, the Fog package layout
  (`gameTreeRoot: ''`) and Fog activation have never run. When FogHost is
  built, its read set must fall within the snapshot roots, or staging fails
  closed. The desktop default config enables Fog, so a default run fails
  until then.
- **No in-app recovery from `modified`.** `/api/game/restore` does not expose
  `overwriteUnrecordedChanges`, and the desktop enables restore only for
  `active`/`interrupted`. A Steam file verification leaves stray files in
  managed roots, so the state stays `modified`.
- **No re-import after a game update.** `version-changed` refuses restore,
  and `importPristine` refuses with `SNAPSHOT_EXISTS`. The only way out is
  deleting the state directory by hand.
- Provenance remains "no legacy artifacts", not "proven vanilla" (no
  reference hashes). The real-data proof used reconstructed, not
  verified-vanilla, data.
- Distribution: a public GPL fork with source tags, and FogMod `dist/` data
  licensing, are unresolved (`docs/LICENSING.md`).
- `engines/item/README.md` and `engines/enemy/README.md` still describe
  legacy vendor probes that the backend no longer uses. These are docs only.

Task 15 completes the desktop controls for the supported combined and Fog
options. The run panel now shows the active game package, generation stage and
percentage, logs, failures, and a persistent generated-package summary. A
generated package can be activated only while the current settings still match
the settings used to generate it; changing options also clears a stale plan
preview. Empty seeds and a configuration with no enabled engine cannot start
generation. Desktop version 0.4.0. Desktop build and typecheck passed
(2026-09-28). Fog-enabled generation remains dependent on an unbuilt FogHost;
Task 16 owns the final end-to-end audit.

Task 14 connects the existing backend pipeline and game-state authority to
the desktop application. The backend exposes state, pristine import, streamed
generation, activation, and vanilla restore endpoints. The desktop sends the
typed configuration, displays progress/logs/errors and the generated package,
and enables activation or restore according to the reported game state. A
backend-owned run directory keeps workspace paths out of the renderer. The
new mutation routes reject ordinary foreign browser origins; packaged file
origin remains accepted. Backend 0.15.0, desktop and contracts 0.3.0.
Focused API integration (1/1), pipeline (4/4), backend/desktop builds and
desktop typecheck passed (2026-09-28). FogHost is still unbuilt, so Fog-enabled
generation reports `FOG_PREREQUISITES_MISSING` until that prerequisite exists.
Desktop packaging/startup and full UI polish remain later tasks.

Task 13 adds the single activation/restore authority in
`apps/backend/src/core/gameState.ts` (see `docs/DECISIONS.md`, "One
backend-owned activation/restore authority"). It covers the following:

- a hash-verified pristine snapshot import that refuses legacy `.bak` chains;
- state detection;
- journaled, atomic, verified activation of a generated package;
- automatic rollback;
- vanilla restore.

Destructive operations refuse unknown, stale or running-game states. The
snapshot is now the pipeline's trusted pristine source: `server.ts` injects
`getPristineSource`. The run manifest records `pristineSnapshotId` and
`gameTreeRoot`. No HTTP/IPC endpoints for import/activate/restore exist yet;
that wiring is Task 14. Reference vanilla hashes do not exist, so provenance
is "no legacy artifacts", not "proven vanilla". Backend `npm test` passed
102/102, including 15 focused authority tests (2026-09-28). `@ds1tr/backend`
is 0.14.0.

Task 12 wires the backend generation sequence in
`apps/backend/src/core/generationPipeline.ts`: DSR verification, a verified
pristine-source gate, combined generation in an isolated workspace, output
hash checks, Fog staging and bounded deterministic retries, final output
checks, a package manifest, and typed progress/log events. `/api/randomize`
accepts the generation request and can stream events with
`Accept: text/event-stream`. It never activates files into the game.

The default pristine-source and Fog-prerequisite providers intentionally
return typed `PRISTINE_IMPORT_REQUIRED` and `FOG_PREREQUISITES_MISSING`
failures. No backend pristine authority or built FogHost/data root exists yet;
supplying an arbitrary path is not proof of clean game data. The pipeline can
be exercised with trusted injected providers, but production generation
requires the two separately recorded prerequisite tasks in
`docs/AI_EXECUTION_PLAN.md`. Combined package checks currently cover the
shared patched files described by the bridge; Fog output checks cover files
declared in the host response. Focused pipeline tests and backend build and
typecheck passed (2026-09-28). `@ds1tr/backend` is 0.13.0.

Task 11 defined and implemented the backend side of the Combined -> Fog
compatibility bridge:

- `apps/backend/src/adapters/fogCompatibility.ts` (pure, no I/O):
  - `buildFogCompatibility` derives the schema-v2 `compatibility` object
    from `patch-manifest.json` and the placements file;
  - `fogReadSet` and `planFogStaging` plan the per-attempt staging and
    merge-base directories;
  - `decideFogRetry` is the bounded, deterministic retry table.
- `seeds.ts` gained `deriveAttemptSeeds`; attempt 0 equals `deriveSeeds`.
- `fogHostProcess.ts` moved to schema v2 and validates `compatibility`. It
  also accepts the new codes `FOG_MERGE_CONFLICT` and
  `FOG_COMPATIBILITY_VIOLATED`.

The host-side rules are specified in `docs/FOGHOST_INTEGRATION.md`
("Compatibility bridge"). They cover the three-way EMEVD merge, preserved
NpcParam rows, stock key-item handling and post-write invariants. Fog
scaling and combined scaling compose (decision in `docs/DECISIONS.md`).

Verification: backend tests 83/83 pass, including the build (2026-09-28).
`@ds1tr/backend` is 0.12.0.

Still open:
- the C# host is unbuilt, so none of the host rules is compiled or tested;
- boss randomization together with Fog is unproven (`11810310` conflict);
- the key-item retry rate is unmeasured;
- FogMod's `scale` mechanism is unread, so scaling composition is a
  hypothesis (open item 8).

Task 10 implemented the backend FogHost process adapter in
`apps/backend/src/adapters/fogHostProcess.ts`. It writes a typed schema-v1
request to a fresh JSON path, launches `FogHost.exe generate`, reads the
authoritative response, forwards structured progress and plain log text,
and reports host crashes and timeouts as non-retryable typed failures. It
rejects stale response paths and invalid request options before launch. The
Fog status probe now points to the planned `tools/fog-host` project and
executable. Seven focused tests, backend build, and backend typecheck passed
(2026-09-28). `@ds1tr/backend` is 0.11.0. The C# host is still unbuilt;
Task 11 owns compatibility inputs and Task 12 owns pipeline wiring.

Task 9 designed the FogHost integration (`docs/FOGHOST_INTEGRATION.md`;
decision in `docs/DECISIONS.md`). It is docs only: no code changed and there
is no version bump. The design is a console host that compiles FogMod's
engine sources at the audited commit `28711c3`, never FogMod's CLI (which
forces DS3) or WinForms. It forces DS1R and exchanges JSON request/response
files. FogMod writes DS1 output in place, so each attempt gets a fresh staging
directory, and the host cleans up FogMod's `.bak` files. Errors are typed and
retry-scoped (`combined` vs `fog`). The overlap analysis shows stock FogMod is
lossy on combined output:
- it replaces EMEVD and keeps only legacy-enemy-randomizer event IDs;
- it deletes the combined engine's scaled `NpcParam` rows;
- it finds key items only in annotated item lots.

FogHost therefore carries a minimal patch set in `GameDataWriter.cs` and
`ItemReader.cs`, and Task 11 defines the bridge inputs. Open items:
- no task builds or vendors the C# host yet;
- FogMod's `dist/` data is game-derived and its distribution is unresolved;
- the SoulsFormats/SoulsIds pins and SoulsIds' license are still to be checked.

Task 8 ran the combined Items + Enemies engine end to end for the first time:
typed request → config → vendored JS generator → real `DsrDataTool`
`scan`/`patch-enemies` → workspace package, via
`apps/backend/scripts/combinedEngineE2eProof.mjs` (harness commit
`75c214dc2656693b49c2861c39f3f2a7e6b8e7a8`; not part of `npm test`). The live Steam install is
legacy-modified (legacy item/enemy randomizers and FogMod applied in place);
against it `patch-enemies` correctly refuses (`DATA_TOOL_FAILED`, enemy model
declaration pruning), and the install stayed byte-identical. On a temp copy
of the tool's read set with the legacy `.bak` files restored, all 47 checks
passed (2026-09-28): generation completes, the package's 54 patched files
match `patch-manifest.json`, reruns are byte-identical apart from embedded
workspace paths and timestamps, real catalogs verify and reuse, failures are
typed and recoverable in the same workspace, and neither game directory
changed. This proves the pipeline on real-format data, not on verified
vanilla data. Findings handed to later tasks (up-front detection of
legacy-modified installs, partial seed directories after a failed patch,
missing dll reported as `DATA_TOOL_FAILED`, volatile JSON fields) are in
`docs/COMBINED_ENGINE_INTEGRATION.md` ("End-to-end generation proof").
No application code changed; `@ds1tr/backend` stays `0.10.0`.

Task 7 connected `CombinedRandomizerAdapter.generate()` to the existing
workspace-only execution bridge. It forwards the typed request and
progress/log events, returns the generated seed package directory on success,
and preserves typed bridge failures including retryability. Two focused
adapter tests passed; backend build and typecheck passed (2026-09-28).
`@ds1tr/backend` is `0.10.0`. Implementation commit:
`57d2f846ca362b91673ffcaf3ae025802e5efb64`. The adapter has not yet
run `DsrDataTool` against a real DSR installation (Task 8); generation is
not connected to activation.

Task 6 resolved the remaining combined-engine blockers that had to be fixed
before a real generation run can be attempted: the combined seed is now a
32-hex-character SHA-256 derivation of the master seed that always satisfies
upstream's charset (Blocker 2); `CombinedRandomizerRequest` gained
`generateSpoilerLog`, mapped straight through instead of pinned off
(Blocker 3); and a reused workspace catalog is verified file-by-file (size +
SHA-256) against the game directory before generation, failing with
`CATALOG_SOURCE_MISMATCH` and never rescanning automatically (Blocker 6).
Backend tests 66/66 pass and the full workspace typecheck is clean
(2026-09-28). `@ds1tr/backend` is `0.9.0`. Implementation commit:
`e2664164d1aa1131952e1ddb1a19a87d960198af`. The user confirmed the seed
derivation (option A); `ARCHITECTURE.md`, `source-analysis.md`, and
`CODEX_PROGRESS.md` now mark the earlier pass-through direction superseded.
Task 7 completed adapter wiring. Still open: a real `DsrDataTool` run
(Task 8), the public GPL fork.

Task 5 extended the focused combined bridge tests (commit
`abeb4c2f56ade130a39e4d42837bf333d586e3e0`); no production code changed.

## Backend adapters

- The minimal `CombinedRandomizerAdapter` boundary is defined for combined item
  and enemy generation requests, progress/log events, and typed results.
- The adapter delegates generation to the workspace-only execution bridge and
  maps its package directory and typed failures to the public adapter result.
- The adapter is not yet invoked by the application pipeline. When called, it
  delegates workspace generation to the bridge; activation remains separate.
- A pure, deterministic mapping layer converts combined requests to the
  upstream item/enemy configuration shape, applies parent `enabled` flags, and
  rejects requests with no enabled randomization category. Task 3 verified
  all required item/enemy combinations, parent gating, deterministic output,
  and the case where only safeguard options are selected. Six focused mapper
  tests and backend build/typecheck passed on 2026-09-28. No generation,
  filesystem, subprocess, or game access was added.
- A pure, deterministic workspace planner
  (`combinedRandomizerWorkspace.ts`) computes the catalog path, per-seed
  output directory, and `scan`/`patch-enemies` argument vectors for a
  generation run, and refuses to plan a workspace located inside the live
  game directory. It performs no filesystem I/O and never constructs
  `install`/`restore` arguments. See
  `docs/COMBINED_ENGINE_INTEGRATION.md` ("Workspace execution proof").
- `kellernz1/ds-randomizer` is selectively vendored (the files needed for
  `DsrDataTool.dll`, plus the minimal JavaScript generation core) and pinned
  at commit
  `f120efe0e95b957b7e76109868c3c9966e22720c` under
  `engines/combined/vendor/` (`UPSTREAM_PIN.md` has the full file list and
  update procedure). `DsrDataTool.dll` builds successfully from this
  vendor directory with a locally-installed .NET 8 SDK; see
  `docs/COMBINED_ENGINE_INTEGRATION.md` ("Vendored build proof") for the
  exact command and artifact path.
- `DsrDataToolExecutor` runs only the planner-provided `scan` and
  `patch-enemies` argument vectors through Node `child_process.spawn`, streams
  typed stdout/stderr log events, emits coarse process-status events, and
  returns typed success, process-failure, or spawn-failure results. It is not
  wired into the adapter or generation pipeline yet. Task 1's implementation
  is in commit `5402b447f3d53a1896ca59267fc5fe10461bc071`; its seven
  focused tests, backend build, and backend typecheck passed on 2026-09-27.
  The task remains blocked because Git could not create `.git/index.lock` to
  commit the task's plan/status update under the current filesystem policy.
- The upstream JavaScript generation core is vendored at the same pinned
  commit under `engines/combined/vendor/src/` — five files, the transitive
  closure of `generate()` and `writeOutput()`
  (`core/{config,generator,output,rng}.js`, `data/prototype-data.js`), with a
  closed import graph of Node builtins only. CLI, HTTP, Electron, share-link,
  catalog-loading, and game-detection modules are deliberately excluded.
- `combinedRandomizerCore.ts` is the backend's single loading boundary for
  that core: a computed dynamic `import()` of `file:` URLs resolved from
  `import.meta.url`, hand-written types for the six upstream exports (so the
  vendored bytes stay byte-identical), and typed `JS_CORE_MISSING` /
  `JS_CORE_INCOMPLETE` failures instead of thrown errors. It loads and
  shape-checks the core; it does not call `generate()` or `writeOutput()`
  yet. See `docs/COMBINED_ENGINE_INTEGRATION.md` ("JS core vendor proof").
- Vendored-file identity is verified by comparing Git blob object IDs against
  the pinned upstream commit, not by `diff`: `core.autocrlf` rewrites line
  endings on Windows checkout. All twelve vendored files verified; object IDs
  recorded in `engines/combined/vendor/UPSTREAM_PIN.md`.
- Documentation correction: upstream `validateConfig(config, { requireGame })`
  **returns** a `string[]` of errors and never throws. The earlier analysis
  said it throws; consumers must check the returned array.
- A minimal combined-engine execution bridge
  (`apps/backend/src/adapters/combinedRandomizerBridge.ts`,
  `executeCombinedGeneration`) now chains all five previously independent
  pieces into one internal path: typed request → pure config mapping → the
  vendored JS core (`normalizeConfig`/`validateConfig`/`generate`/
  `writeOutput`) → the pure workspace planner → the `DsrDataTool` executor
  (`scan` when the workspace has no catalog, then `patch-enemies`). It
  returns a typed success carrying the package directory, placements path,
  `patch-manifest.json` path, upstream version, placement hash, and seed, or
  one of fifteen documented failure codes (`CATALOG_SOURCE_MISMATCH` added
  by Task 6). Only `GENERATION_FAILED` is
  marked retryable. See `docs/COMBINED_ENGINE_INTEGRATION.md` ("Execution
  bridge proof") for the exact data flow.
- Generation is proven workspace-only against fixtures: 19 focused tests run
  the real vendored `generate()`/`writeOutput()` in a temporary workspace
  with a synthetic schema-17 catalog, faking only the `DsrDataTool` process
  layer. They assert a real package is produced with
  `dataStatus: "extracted"`, that the process layer receives the planner's
  exact `scan`/`patch-enemies` argument vectors and nothing else, that the
  placements file exists before `patch-enemies` is invoked, that a fixture
  game directory is byte-identical before and after a successful run, and
  that the same request twice yields the same placement hash. Backend tests
  54/54 pass; backend build and the full workspace typecheck are clean
  (2026-09-28).
- The bridge rejects `dataStatus !== "extracted"` before writing any file and
  before spawning any process, so integration Blocker 1's silent prototype
  fallback can no longer reach a package directory. It also pre-checks
  `catalog.schemaVersion !== 17` so an obsolete catalog reports
  `CATALOG_STALE` instead of an opaque generation error.
- The bridge plans the workspace from the **normalized** upstream seed rather
  than the raw request seed, because upstream derives its own
  `seed_<safeSeed>` package directory from the normalized value; planning
  from the raw value would point `--placements` at a file upstream never
  wrote. See `docs/DECISIONS.md`.
- Three upstream config keys are pinned by the bridge rather than inherited
  from `defaultConfig`: `generateSpoilerLog: false` (no request field exists —
  Blocker 3 — and the upstream default `true` would emit an unrequested
  spoiler file), plus `useExtractedData: true` and `dryRun: false`, both
  verified inert at the pinned commit (read nowhere in the vendored core or
  `DsrDataTool`).
- Fixed: `core/pipeline.ts`'s combined-engine probe pointed at
  `vendor/src/cli.js` and `vendor/DSR-Randomizer.exe`, neither of which
  exists or should, so engine status always reported the combined engine as
  missing. It now probes `vendor/src/core/generator.js` and the built
  `vendor/tools/DsrDataTool/bin/Release/net8.0/DsrDataTool.dll`, with a test
  asserting the combined engine reports `source` mode. The bridge itself
  never consults `ExternalAdapter`.
- Still not done: the bridge has never executed the real `DsrDataTool` against
  a real Dark Souls installation, so a genuine `scan`/`patch-enemies` round trip is
  unproven. Blockers 2, 3, and 6 were resolved by Task 6 (see
  `docs/COMBINED_ENGINE_INTEGRATION.md`, "Blocker resolution"); the public
  GPL fork remains open.
