# Fog engine integration point

FogMod is Apache-2.0 licensed upstream. Planned integration is a dedicated headless C# host around `FogMod.Randomizer`, not WinForms automation.

The backend process adapter invokes `FogHost.exe generate --request <json> --response <json>`.
The development status probe looks for either:

- `tools/fog-host/FogHost.csproj`
- `tools/fog-host/bin/Release/net8.0/FogHost.exe`

The host project is not yet built or vendored. See `docs/FOGHOST_INTEGRATION.md`.
