# FogHost

A headless Dark Souls Remastered host around the pinned FogMod engine
(`6e6dc78`). The backend runs it through
`apps/backend/src/adapters/fogHostProcess.ts`:

```text
FogHost.exe generate --request <request.json> --response <response.json>
```

- The contract, merge rules and invariants are in
  `docs/FOGHOST_INTEGRATION.md`.
- Pins and upstream modifications are in `vendor/UPSTREAM_PIN.md`.

## FogMod data root (not bundled)

FogMod's `dist/DS1R` files are FromSoftware-derived and are never committed
or distributed. To supply them:

1. Get the folder that contains `dist` from
   https://github.com/thefifthmatt/FogMod at commit `6e6dc78`, for example
   with `git archive 6e6dc78 dist/fog.txt dist/DS1R`.
2. Point `DS1TR_FOG_DATA_ROOT` at that folder.

FogHost checks the 59 files it reads against the SHA-256 manifest in
`src/DataRoot.cs` on every run. A missing, altered or unexpected file fails
with `FOG_DATA_MISSING` and a list of the problems. For `.txt`/`.xml` files,
the check ignores line endings.

## Build and test

Use the .NET 8 SDK (`docs/DECISIONS.md`; the project-owned copy is
`.workspace/sdk/dotnet.exe`, see `docs/WORKSPACE.md`):

```sh
dotnet build tools/fog-host/FogHost.csproj -c Release          # -> bin/Release/net8.0/FogHost.exe
dotnet build tools/fog-host/tests/FogHost.Tests.csproj -c Release
DS1TR_FOG_DATA_ROOT=<fog data> tools/fog-host/tests/bin/Release/net8.0/FogHost.Tests.exe
```

- The self-tests never touch a game installation.
- Without `DS1TR_FOG_DATA_ROOT`, the data tests are reported as skipped.
- A proof on real-format data is at `apps/backend/scripts/fogHostE2e.mjs`.
- The Compact Fog regression tests (catalog v3, start reachability, the original hard-lock, the V5 smoke seed, a Ceaseless-rejected world) and the V5/V6
  generation tests need real data: set `DS1TR_COMPACT_DATA_ROOT` (a prepared Compact world-data folder), `DS1TR_PRISTINE_DSR_ROOT` and
  `DS1TR_TEST_GAME_ROOT` (a clean Dark Souls Remastered copy) and `FOGHOST_EXE`. Without them those tests are not run.
- Annotation sources a request may name: `legacy-fogmod` (profiles v1/v2), `compact-v1` (v3/v4), `compact-v2` (v5, with the frozen Ceaseless post-boss check)
  and `compact-v3` (v6: physically sound Asylum start, `StartReachabilityValidator`). Acceptance sample for v6:
  `DS1TR_COMPACT_V3_SEEDS=1,2,3 FogHost.Tests.exe compact-v3-seed-sweep`. Design: `docs/COMPACT_FULL_FOG_V3.md`.
- `upstream-v7` (v7): the original FogMod `dist/fog.txt` (bundled in `upstream/`, see `upstream/PROVENANCE.md`) read unchanged, with the 58 inputs rebuilt from the
  installed game (`reconstruct-compact-data`); no Compact event reconstruction runs. `FogHost upstream-projection --data-root <dir> --out <json>` exports the upstream
  key items and lots for the backend. The V7 tests (`tests/UpstreamV7Test.cs`) need `DS1TR_FOG_DATA_ROOT` (a data root holding the pinned fog.txt),
  `DS1TR_PRISTINE_DSR_ROOT`, and `DS1TR_V7_DATA_ROOT` for the data-root check; `DS1TR_V7_ACCEPTANCE_SEEDS=300` runs the raw-seed acceptance. Design: `docs/V7_UPSTREAM_PARITY.md`.
