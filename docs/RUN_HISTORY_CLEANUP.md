# Run history cleanup

The history grows with every generation. This describes exactly what a run owns, what the cleanup may delete and what it never touches (`apps/backend/src/core/historyCleanup.ts`, tested against real folders in `historyCleanup.test.mjs`).

## What the application stores per run

| Where | What | Owned by the run |
|---|---|---|
| `<state>/history/runs.json` | one record: id, time, Seed Code, profile, master seed, settings, status (`generated`/`applied`/`restored`), `appVersion` (since 0.42.0), paths | yes |
| `<state>/runs/<uuid>/` | the **run folder** the generation wrote into: the package (`fog-N-M/game`, `run-manifest.json`), the attempt folders (`combined-N`), the spoiler, `hints.json`, `failed-manifest.json` (and, in internal QA builds only, `solver-witness.json`, `solver-route.md`; `docs/INTERNAL_QA.md`) of a failed attempt | yes |

## What the application stores that a cleanup never touches

- `<state>/installs/<id>/pristine`, `snapshot.json` and `activation.json`: the one backup authority and the record of what is applied. **Restore needs only these**, not the run folder.
- `<state>/world-data/`: the prepared Fog data shared by every run of a profile (including the V7 set with the bundled annotation).
- the Dark Souls installation and its saves; the user's settings and Skill Issue preferences; support bundles; release artifacts; the application and its logs.

## The actions

- **Clear history** ("Wyczyść historię"): asks the backend for a preview (how many entries go, how many stay, how many run folders nobody points at), shows a confirmation with those numbers, and only then removes every entry that is not protected, the run folders those entries own, and the folders no entry points at (failed or interrupted runs, older than ten minutes so a generation that is still writing is never touched).
- **Remove old runs** ("Usuń stare runy", shown only when such runs exist): the same, restricted to runs whose generation profile is not the current one; folders without a record are left alone.
- **Delete from history** (one run, in its menu): confirmation, then the record and the run folder (the folder is kept if another record still points into it).

## The applied run is protected

An entry is protected when its status is `applied` **or** its package is the one an installation's activation journal names (or lies inside its run folder). A protected entry and its folder are never removed by a clear, and a single delete is refused with a message ("Restore the original files first"); the menu item is disabled for it. Restore, which works from the pristine snapshot, can therefore never be affected, and the applied run stays in the list.

## Safety properties (each is a test)

- nothing changes before the person confirms (the preview only reads; cancelling or pressing Escape closes the dialog);
- a folder is deleted only when it is a direct child of the run root; a record that points elsewhere loses its record but never an outside folder;
- the history record is removed first, so a folder that cannot be deleted never leaves a dead record;
- shared data (pristine snapshot, journal, world data, settings, support bundles) is identical before and after.

## What the list shows

Each run shows the application version it was made with (`appVersion`, recorded since 0.42.0; older entries say "version unknown"), the generation profile and, when the profile is not the current one, a quiet label ("Starszy profil"). The label does not mean the Seed Code is invalid: older profiles replay as before.
