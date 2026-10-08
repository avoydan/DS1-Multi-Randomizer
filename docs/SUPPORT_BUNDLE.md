# Support bundle

Status labels: **AUTOMATED VERIFIED**, **STATICALLY VERIFIED**, **GAMEPLAY UNVERIFIED**, **PARTIAL**, **DEFERRED**, **UNSUPPORTED**.

*Advanced → Diagnostics and support → Export support bundle* (or *… → Export support bundle* in a history row) makes **one ZIP** that a person can give to whoever helps them. **Nothing is sent anywhere**: the main process shows a save dialog and the person decides who receives the file.

## 1. The allowlist

The bundle is assembled from **named entries only** (`BUNDLE_ALLOWLIST` in `apps/backend/src/core/supportBundle.ts`); no folder is walked, so a file that is not on the list cannot get in. Entries exist only when the data exists and is useful:

| Entry | Content |
|---|---|
| `README.txt` | what the bundle is, what is in it, what is never in it, what was removed (in the app language) |
| `summary.txt` | the diagnostics as readable text (in the app language) |
| `diagnostics.json` | the structured model (`docs/DIAGNOSTICS.md`), canonical, English field names |
| `generation/run-manifest.json` | the manifest of the described world, or of the **last failed generation** when it is newer; attempts, validation notes, **package file hashes** |
| `generation/generation-summary.json` | the last generate / apply / restore / failure (codes, stages, timings, counts) |
| `generation/validation-summary.json` | validation reason codes, attempts, enemy / key placement validation, warnings and errors |
| `logs/app.log`, `logs/backend.log`, `logs/relevant-generation.log` | the newest lines (size-limited) |
| `release/release-manifest.json` | the build information and signing state |

Reproduction data is included on purpose: **Seed Code**, generation profile, canonical resolved settings, versions, package hashes, error codes and the context around them.

## 2. What is never in it

Pristine or modified Dark Souls files, MSB / EMEVD / ESD / PARAM data, the Fog upstream payload, executables, the game folder, certificates or private keys, environment variables, API keys, tokens, cookies, browser data, unrelated files, the whole run folder. The test plants a "game file" and a "run package" next to the files it reads and searches the archive for their content.

## 3. Redaction (`core/redaction.ts`)

Every text is redacted; JSON is parsed and every string (and key) redacted so it stays valid JSON.

* **Folders in every spelling** (backslash, forward slash, JSON-escaped `\\`, `file:///`, URL-encoded): the game folder → `<GAME_DIR>`, the user-data folder → `<USER_DATA>`, the temp folder → `<TEMP>`, the app runtime → `<APP_RUNTIME>`, any profile → `C:\Users\<USER>\…` (names with spaces too); `/home/x` and `/Users/x` likewise.
* **Identity:** the Windows user name and computer name as whole words → `<USER>`, `<HOST>` (an item called *Adamantite* is not touched); e-mail → `<EMAIL>`; MAC and IP addresses (v4 and v6; a time like `18:30:00` is not an IPv6 address) → `<MAC>`, `<IP>`; braced machine GUIDs → `<ID>`.
* **Secrets:** `key=value` and JSON pairs whose key contains `api_key`, `secret`, `token`, `password`, `cookie`, `authorization`, `session id`, `credential`, `private key`, `CSC_LINK`, `CSC_KEY_PASSWORD`, thumbprint, machine GUID, serial number…; `Authorization: Bearer …`; cookie lines; URL credentials and secret query parameters; keys in well-known formats (`sk-…`, `sk-ant-…`, `ghp_…`, `xox?-…`, `AKIA…`, Google API keys, JWTs, PEM private keys); **signing files** (`*.pfx`, `*.p12`, `*.pem`, `*.snk`, `*.cer`, `*.key`) → `<SIGNING_FILE>`; the **exact values** of the localhost session token (`DS1TR_API_TOKEN`) and of every secret-looking environment variable, wherever they appear.
* **Kept on purpose:** Seed Codes, SHA-256 hashes, versions, timestamps, run / operation ids.
* **Independent check:** after assembling, `findLeaks` scans every finished entry for the user name, host name, every known folder (all spellings) and every known secret value; **one hit and the bundle is refused** (`BUNDLE_SANITIZATION_FAILED`, nothing saved). The archive is then read back (`readZip`: allowlisted names, checksums).
* The redactor is bounded (no unbounded backtracking; lines over 8,000 characters are cut) so a huge log cannot stall it.

## 4. Size

Per entry: README 32 KiB, summary 64 KiB, diagnostics 256 KiB, manifest 256 KiB, generation summaries 128 KiB, `app.log` 256 KiB, `backend.log` 512 KiB, generation log 256 KiB, release 64 KiB; total **2 MiB** uncompressed (over the limit: `BUNDLE_TOO_LARGE`). A log over its limit keeps its **newest** lines and starts with a note. The ZIP itself is deflated and typically well under 100 KiB.

## 5. When it works

* after a successful generation, a world applied or restored;
* after a **failed generation** (the last failed attempt, its stage and code, the failed manifest, the generation log);
* after an **Apply failure** (`APPLY` stage, e.g. `GAME_RUNNING`);
* with **no world yet** (diagnostics, README, summary, whatever logs exist).

## 6. Tests (AUTOMATED VERIFIED)

`supportBundle.test.mjs` builds real files with **synthetic secrets** (`ANTHROPIC_API_KEY=sk-ant-…`, `password=…`, `token=…`, `Bearer …`, `sessionToken: …`, a Cookie line, the session token, `C:\Users\<real user>\…` in backslash and forward-slash form, the host name), creates the bundle, unzips it and searches **every entry** for every secret, folder and the "game file" marker; checks the allowlist, the failed-generation / Apply-failure / no-world cases, the size cuts, refusal of non-allowlisted names, and the ZIP round trip. `redaction.test.mjs` covers each pattern, spellings, JSON validity, word-boundaries, Seed Code protection and counters. The packaged app's backend was also run and its bundle searched for the user name, the game folder, the user-data folder and the live session token (`docs/IMPLEMENTATION_STATUS.md`).

## 7. Limits

* **PARTIAL:** redaction is pattern- and known-value-based. A secret in an unknown format with no `key=` label and not present in the environment would not be recognised; the bundle contains no free-form user text, only application-produced logs, manifests and diagnostics, and the person can read the files before sharing them (the README says so).
* **DEFERRED:** a "preview before saving" dialog listing the entries.
