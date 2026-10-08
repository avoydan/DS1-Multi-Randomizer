# Diagnostics and logging

Status labels: **AUTOMATED VERIFIED**, **STATICALLY VERIFIED**, **GAMEPLAY UNVERIFIED**, **PARTIAL**, **DEFERRED**, **UNSUPPORTED**.

Diagnostics exist to find a problem, never to change a world. Nothing here is part of a Seed Code, a package or a hash. There is **no telemetry and no upload**: everything stays on the machine until the person exports it (see `docs/SUPPORT_BUNDLE.md`).

## 1. One semantic source

Generation produces its metadata **once**: the run manifest (`run-manifest.json` in the package, written by the pipeline), the placements document (`randomizer.json`), the hint data (`hints.json`, `docs/HINT_SHEET.md`) and the operation records (`diagnostics/last-run.json`, below). The plan preview, the hint sheet, the diagnostics view and the support bundle **read these**; none of them re-reads modified game files or the generated package.

## 2. The model (`Diagnostics`, `packages/contracts/src/diagnostics.ts`)

`GET`-like endpoint `POST /api/diagnostics` (`{ gamePath, language, worldId? }`) returns, already redacted:

* **application**: desktop / backend / contracts versions, generation profile, engines (combined randomizer version, FogHost project version from the build info and the host version, FogMod commit, world data id), build commit, **install mode** (`installed | portable | development | unknown`), the interface language (a diagnostic fact only), platform.
* **game**: path configured (never the path), state (`vanilla | active | interrupted | modified | version-changed | invalid | no-snapshot`), pristine snapshot saved, applied / restored.
* **world** (the applied world, else the newest, or `worldId`): Seed Code, master seed, profile, status, creation time, run id, snapshot id, the app version at generation, canonical settings, **package summary** (file count, total bytes, a SHA-256 over the file list), placement validation summary, key placement summary, **fog status** (enabled, attempts, retry counts, engine), the attempts with their result codes, the model-budget/enemy placement summary (bounded in size), validation notes, hint sheet availability.
* **generation**: the last generate / apply / restore and the last failure: result, **structured code**, **stage**, **stage timings**, event counts and **event windows** per stage, warnings and errors (bounded), the operation id.
* **release**: signing state, artifact version, build mode, build time.

`schemaVersion: 1`; names and enum values are stable and never translated.

**Release information at run time.** `apps/desktop/scripts/package-win.mjs` writes `runtime/build-info.json` (version, git commit, `development | public` mode, component versions, build time) into the package. The signing state is derived from it: a development build is `unsigned`; a public build (`--release`) is only produced after `tools/release/release-verify.mjs` required a trusted signature on every executable and artifact, so it reports `signed`. No certificate, key or path of a signing identity is ever read at run time.

## 3. In the interface

*Advanced → Diagnostics and support* (and *… → Diagnostics* in a history row for that world): a **friendly summary first** (healthy / the last operation failed with its translated message and code, application, installation, signature, game state, world), **Technical details** behind a toggle (the full text), **Copy diagnostics** (the same text, labels in the app language, codes/versions/ids untouched) and **Export support bundle**. The view re-reads when the world or the game changes.

## 4. Logging

* **Format:** one JSON line per event: `time, level (debug|info|warning|error), stage, message, correlationId?, code?, source?`.
* **Stages:** `GAME_DETECTION, SNAPSHOT, ITEM_PLAN, ENEMY_PLAN, FOG_PLAN, DATA_TOOL, MERGE, PACKAGE, APPLY, RESTORE` (+ `SEED, VALIDATION, SERVER, APP`). Pipeline events are mapped by `logStageFor`; the data tool and FogHost lines arrive tagged with their component.
* **Files:** `<userData>/logs/app.log` (desktop main process), `<userData>/state/logs/backend.log` (backend), `<run folder>/generation.log` (the lines of one generation, outside the package). Size-limited with rotation (512 KiB / 1 MiB, two rotated files). `debug` lines are written only with `DS1TR_DEBUG=1`.
* **No secrets in logs:** the backend log passes every line through the redactor's secret patterns and the exact values of the session token and of secret-looking environment variables; the desktop log records only what the main process does.
* **Correlation id:** the backend assigns a UUID to every generate / apply / restore operation. It is in every log line of that operation, in the result sent to the interface (`correlationId`), in lines the interface reports back (`POST /api/log`), in the operation record and in the support bundle. It is **diagnostics only**: the code that derives seeds and settings never mentions it (tested), so two runs of the same world cannot differ because of it.
* **Operation records:** `diagnostics/last-run.json` keeps the last generate / apply / restore and the **last failure**, so a failed generation, a failed Apply and the state before any world exists can all be diagnosed. A failed attempt's manifest is kept next to the run (`failed-manifest.json`).

## 5. Tests (AUTOMATED VERIFIED)

`operations.test.mjs` (JSON lines, redaction at write time, rotation, stage mapping, timings, counts, windows, last failure survives a success, no correlation id in seed code), `diagnosticsText.test.mjs` (the texts in both languages, no raw keys, no folder), `supportBundle.test.mjs` (diagnostics without any folder), `server.integration.test.mjs`.
