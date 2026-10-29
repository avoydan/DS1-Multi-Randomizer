# Ceaseless single-case QA preparation (2026-10-29)

## State

**PARTIAL.** The selected package remains verified, the exact external case descriptor and one-click fail-closed preflight are prepared, and the offline trace/import tests pass. No gameplay telemetry was captured. `START-CEASELESS-QA.cmd` will stop before starting a game because the established QA root is not a complete runtime and there is no independently verified telemetry offset profile.

No application code, production validator, V1–V5 semantics, generation profile, retry limit, public version, safe baseline tag, QA game data, live Steam data, or game process was changed or accessed.

## Repository and baseline checks

- V5 worktree: `C:\Users\Adam\VisualStudioProjects\ds1-triple-randomizer-v5-isolation`
- branch: `fix/compact-fog-v5-progression`
- HEAD: `94cd804dc838437f4c91969fb8ccf6d2be0cb7c6`
- `v5-safe-ceaseless-baseline` still points to `abc6a5286353618a1087b8363bf9438236b8fb10`.
- The source package was produced at `c7440e685221ec04b7856a317aa8037c7142ce8d`; this task did not move the baseline tag.

## Selected package and corrected report hash

External package:

`C:\Users\Adam\AppData\Local\Temp\ds1tr-ceaseless-certified-v2-lord-souls-off-05634a7e78b443e6910b317721e86b50\sweep-1-certified-lord-souls-off-1\package`

External descriptor: `C:\Users\Adam\AppData\Local\Temp\ds1tr-ceaseless-certified-v2-lord-souls-off-05634a7e78b443e6910b317721e86b50\sweep-1-certified-lord-souls-off-1\qa-telemetry\case.json`; descriptor SHA-256 `2b1ff04c7e8b8cef8b6fa61859128911a195dee0cf7ee31b5e9d3f480ddae9ab`. The tool pins this exact descriptor hash in code, so editing its target paths/options invalidates preflight.

| Identity | Verified value |
|---|---|
| Seed Code | `DS1TR1-06NWCQQ3-K2HDEE69-N52JB6B9-K6MJKEN0-EJM0PAGA-HDTZ7MYJ-EG6METX0-J590788Q-2PHNBM38-2MTF0N8G-5K6N1A8E-08042ASS-WW` |
| Master seed | `certified-lord-souls-off-1` |
| Combined seed | `8a3d20e4760831c1eccf67c75a417193` |
| Raw Fog seed / accepted attempt | `3695053466` / `4` |
| Profile | `ds1tr-gen-v5` |
| Package tree SHA-256 | `32f0c2afeff25c9341405989b80ba7dc6a72da1f3e90ecdf90cf37ca2b5f79a9` |
| `run-manifest.json` SHA-256 | `e81a504da9c251d9e784eb6708133aa3e98a34cd4df9757d4eee3e259cf7324d` |
| Contemporaneous V2 attestation SHA-256 | `6d4a2fb7eb0445b92c5b729b16b84ae23b6316bb56b53107a3d282a5870ae4a4` |
| Files / bytes | 74 / 78,082,827 |
| Per-file verification | 0 mismatches against the run manifest and V2 attestation; 0 reparse points |

The earlier short report `32f0c2afeff25c93478?` was truncated while formatting. The complete value above was recomputed from the current package inventory using the same canonical path/size/file-hash digest used by the attestation, then compared with the attestation. The manifest and attestation hashes were also recomputed. The V2 attestation explicitly says it has no external signature; it is local contemporaneous evidence, not a cryptographic signature from an independent authority. Descriptor, attestation, manifest, and package files are read in separate filesystem operations, so these checks are not an atomic snapshot and do not protect against an adversary mutating files concurrently. Run verification against quiescent local inputs.

The package records backend `0.46.4`, FogHost project `0.7.4`, FogHost host `0.7.3`, FogMod pin `6e6dc7886d0f8bcc53c1800d95c9e859d1f2f87f`, catalog fingerprint `73f3c842d51490d45a836a489105b37f57d702eaa2ffda084f42781e5aaef00f`, semantic fingerprint `6c73ee8777629c43759403d0c156cd928107cfcc8d21439ca5f1eff09686e5f0`, and world-data ID `ef625777d6aa5abf`.

Expected route identity: event flag `11410410`, entrance `o4961_0000`, graph-side destination `Demon Ruins Lava`. That is the generated graph destination label; actual landing and physical traversal remain unverified.

## QA runtime fail-closed result

Locked executable: `C:\Users\Adam\ds1tr-qa\game-inst\DARK SOULS REMASTERED\DarkSoulsRemastered.exe`

Expected executable SHA-256: `a45aaa36dd2f6cc151670a639ea5547043cf38ea79ff4178b963c6ed71f98d7b`.

The QA root is an extracted data-only snapshot: 199 files, approximately 151,579,320 bytes, with `event`, `map`, `msg`, `param`, `paramdef`, `script`, `sfx`, and the executable. It has no matching top-level `.bhd`/`.bdt` game-data archive pair and no complete collision/NVM/HKX runtime assets. The executable by itself is not treated as a complete playable installation. The directory and executable were only inspected/read; neither this source nor Steam was modified.

Even if archives later appear, the helper still refuses until an exact full-runtime inventory/build profile is independently pinned. It rejects Steam-path segments, mismatched executable path or SHA-256, reparse paths, and hard-linked certified files. No attempt is made to repair, copy, activate, restore, launch, or attach to the game.

## Telemetry implementation and boundaries

Standalone tool: `tools/qa-telemetry`, version `0.1.0`, .NET 8, BCL-only, no reference to production projects. It is not wired into the desktop app, backend, activation, or release packaging.

- Process rights requested by this build: **none (`0x0000`)**.
- The documented future allowlist is query-limited-information (`0x1000`) plus read-only memory (`0x0010`) only. No process-open/read-memory call exists in this build.
- No input automation, memory mutation, injection, or game-file writes are implemented.
- Map identity, player XYZ, event flag `11410410`, map transitions, barrier/object state, interaction state, and all four required marker validations are explicitly `UNVERIFIED`. No offset source, pointer chain, type, or validation procedure was guessed.
- The live trace writer is disabled. Its synthetic-only writer uses `CreateNew`, appends compact one-record-per-line JSONL, and creates a session summary plus separate summary SHA-256 file. These hashes detect post-write changes; they do not make local data tamper-proof.
- The analysis importer verifies trace/session/package hashes, sequence, session ID, schema, provenance, and declared observation-status fields. It does not independently validate known-marker procedures. A synthetic trace returns `SYNTHETIC_TEST_PASS` only; any live-provenance input remains `TELEMETRY_UNVERIFIED`. This build has no `GAMEPLAY_CONFIRMED` promotion path.
- The importer associates a future validated route trace with these analysis selectors: `boss-completion:11410410`, `post-boss-approach:o4961_0000`, `post-boss-crossing:o4961_0000`, `arrival:main-demon-ruins`, and `route:main-to-lower-lava`. It emits `UNKNOWN` for each unless a future independently validated profile is pinned and every route checkpoint passes. These selectors are analysis-side classifications, not a claim that current opaque per-package witness edge IDs have already been matched. The importer does not modify the physical-witness graph or production validator.

## Marker validation required before telemetry can be trusted

The locked executable needs an independently derived profile and a complete isolated runtime first. Then validate, in this order, before gameplay evidence is trusted:

1. a deterministic known map such as Firelink;
2. player coordinates change consistently during ordinary movement;
3. one known vanilla map transition changes map identity correctly;
4. a reproducible known event-flag state reads correctly, including the `11410410` set state after the boss;
5. if barrier/action markers are later added, validate them independently against an observable known state.

The profile source, full pointer chain, value type, bounds, marker procedure, and exact executable SHA must be reviewed and recorded before setting any field or profile to `VERIFIED`. The telemetry reader must not certify its own offsets by comparing only against values it read through those same offsets.

## One gameplay case and outcome rules

After the runtime/profile gate is genuinely cleared, collect one case with these checkpoints:

`CEASELESS_PRE_FIGHT` → `CEASELESS_BOSS_ACTIVE` → `CEASELESS_DEFEATED` → `EXIT_APPROACH` → `EXIT_PROMPT_PRESENT` → `EXIT_CROSSED` → `DESTINATION_LANDED` → `MAIN_DEMON_RUINS_REACHED` → `LOWER_LAVA_REACHED` → `POST_RELOAD_EXIT_VERIFIED`.

The intended gameplay sequence is to use the exact locked package, enter and defeat Ceaseless, observe flag `11410410`, approach and interact with the expected post-boss exit, cross it, verify valid landing and reach both the main Demon Ruins and lower lava route without an artificial warp, then reload and repeat the exit check. Pair observations with owner video/screenshots and record death, teleport, loading, or sampling gaps so those cannot be mistaken for traversal.

Pass requires every checkpoint and marker to be verified, flag `11410410` observed set, the exit actionable, a successful transition and valid landing, the expected physical route reached, and post-reload progression retained.

Keep these failures distinct: `NO_EXIT_PROMPT`, `EXIT_NOT_ACTIONABLE`, `TRANSITION_FAILED`, `WRONG_DESTINATION`, `UNDER_MAP_LANDING`, `LANDING_ISOLATED`, `MAIN_ROUTE_UNREACHABLE`, `LOWER_LAVA_UNREACHABLE`, `POST_RELOAD_FAILURE`, `TELEMETRY_UNVERIFIED`, and `PACKAGE_IDENTITY_MISMATCH`.

## Upstream comparison preparation

The case descriptor preserves raw Fog seed `3695053466` and the accepted attempt `4`. The corresponding selected V5 settings are: traversable fogs, boss fogs, area warps, scaling, and DLC enabled; Major/Minor PvP, Lordvessel gates, random start, disconnected fogs, glitched logic, and Lord Souls requirement disabled; pacifist mode disabled. These are ready to map to the pinned FogMod settings for a later single-seed comparison. No upstream FogMod run was performed, and no topology parity is claimed.

## Tests and remaining action

Focused offline self-tests cover exact package/manifest/attestation hashes, changed bytes, path escape, query/read rights allowlist, refusal of excess process rights, Steam path, runtime archives/profile, executable path/hash, append-only/no-overwrite trace creation, session and summary hashes, corruption, case mismatch, unknown checkpoints, missing completion summary, and the non-promotion of synthetic/unverified traces.

The one next owner action is to provide or identify a **complete isolated non-Steam QA DSR runtime** (with its original data archive pair and collision/runtime assets), separate from the read-only extracted source and live Steam. Do not run the current game test against the incomplete root. After a complete runtime is available, additional implementation and review are still required: pin its full inventory, derive and independently validate exact-build markers, and add a live read-only telemetry path. This helper cannot currently open/read a process or produce a trusted gameplay trace; only then can the single Ceaseless gameplay case be run with trustworthy telemetry.
