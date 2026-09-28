# Fog engine integration point

FogMod (Apache-2.0) runs inside FogHost (`tools/fog-host`), a headless .NET 8 host that
compiles the pinned FogMod engine sources (commit `6e6dc78`, the last working DS1R
revision) — never FogMod's WinForms UI or CLI.

The backend invokes `FogHost.exe generate --request <json> --response <json>` through
`apps/backend/src/adapters/fogHostProcess.ts`. The engine status probe looks for:

- `tools/fog-host/FogHost.csproj`
- `tools/fog-host/bin/Release/net8.0/FogHost.exe`

Fog generation also needs `DS1TR_FOG_DATA_ROOT`: a folder containing FogMod's `dist`
at `6e6dc78`, supplied by the user (never bundled). See `tools/fog-host/README.md` and
`docs/FOGHOST_INTEGRATION.md`.
