# Sets the environment of a developer shell to the project-owned workspace (docs/WORKSPACE.md).
#   . .\tools\devenv\dev-env.ps1
# Everything it points at lives inside this repository under the git-ignored `.workspace` folder.
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$ws = Join-Path $root '.workspace'
$env:DS1TR_WORKSPACE = $ws
$sdk = Join-Path $ws 'sdk'
if (Test-Path (Join-Path $sdk 'dotnet.exe')) {
  $env:DS1TR_DOTNET_SDK = Join-Path $sdk 'dotnet.exe'
  $env:DOTNET_ROOT = $sdk
  $env:DOTNET_CLI_HOME = Join-Path $ws 'tooling\dotnet-cli-home'
  $env:NUGET_PACKAGES = Join-Path $ws 'nuget'
  $env:DOTNET_MULTILEVEL_LOOKUP = '0'
  $env:DOTNET_NOLOGO = '1'
  $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
  $env:PATH = "$sdk;$env:PATH"
}
# A clean Dark Souls Remastered data subset for the golden compatibility fixtures and real-generation tests.
$game = Join-Path $ws 'qa\game-inst\DARK SOULS REMASTERED'
if (Test-Path $game) { $env:DS1TR_GOLDEN_GAME_SOURCE = $game }
$env:TEMP = Join-Path $ws 'tmp'; $env:TMP = $env:TEMP
New-Item -ItemType Directory -Force $env:TEMP | Out-Null
Write-Host "DS1TR workspace: $ws"
