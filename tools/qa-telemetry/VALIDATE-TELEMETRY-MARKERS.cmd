@echo off
setlocal
set "TOOL_DIR=%~dp0"
set "TOOL_DLL=%TOOL_DIR%bin\QA-0.5.0\QaTelemetry.dll"
set "DOTNET=C:\Users\Adam\dn8sdk\dotnet.exe"

title DS1 Triple Randomizer - vanilla telemetry calibration
echo Exact-build telemetry calibration on the clean vanilla QA runtime
echo This workflow does not use the certified randomized package or load the Ceaseless world.
echo.

if not exist "%TOOL_DLL%" (
  echo BLOCKED: telemetry helper has not been built.
  echo Build: dotnet build tools/qa-telemetry/QaTelemetry.csproj --configuration Release --output tools/qa-telemetry/bin/QA-0.5.0
  goto :failed
)
if not exist "%DOTNET%" (
  where dotnet >nul 2>nul
  if errorlevel 1 (
    echo BLOCKED: .NET 8 runtime was not found.
    goto :failed
  )
  set "DOTNET=dotnet"
)

echo [1/4] Verify the pinned clean-runtime manifest, exact executable, and required files
"%DOTNET%" "%TOOL_DLL%" verify-calibration-runtime
if errorlevel 1 goto :failed

echo.
echo [2/4] Audit signature and pointer definitions against the clean QA executable
"%DOTNET%" "%TOOL_DLL%" audit-build --target clean
if errorlevel 1 goto :failed

echo.
echo [3/4] Verify same-user Steam, provision or verify the clean-QA-only AppID sidecar, and launch the exact clean executable
"%DOTNET%" "%TOOL_DLL%" launch-calibration
if errorlevel 1 goto :failed

echo.
echo [4/4] Begin owner calibration using vanilla progression
echo Load a vanilla save/world. Do not load any generated package world.
"%DOTNET%" "%TOOL_DLL%" calibrate-markers
if errorlevel 1 goto :failed

echo.
echo Executable telemetry profile verified. Save the emitted profile path for a later Ceaseless QA session.
pause
exit /b 0

:failed
echo.
echo Calibration stopped. No package runtime or live Steam DSR was selected by this launcher.
echo The live reader uses PROCESS_QUERY_LIMITED_INFORMATION and PROCESS_VM_READ only.
pause
exit /b 1
