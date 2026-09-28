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

Use the .NET 8 SDK (`docs/DECISIONS.md`; on the reference machine it is
`~/dn8sdk/dotnet`):

```sh
dotnet build tools/fog-host/FogHost.csproj -c Release          # -> bin/Release/net8.0/FogHost.exe
dotnet build tools/fog-host/tests/FogHost.Tests.csproj -c Release
DS1TR_FOG_DATA_ROOT=<fog data> tools/fog-host/tests/bin/Release/net8.0/FogHost.Tests.exe
```

- The self-tests never touch a game installation.
- Without `DS1TR_FOG_DATA_ROOT`, the data tests are reported as skipped.
- A proof on real-format data is at `apps/backend/scripts/fogHostE2e.mjs`.
