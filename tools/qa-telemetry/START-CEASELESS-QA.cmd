@echo off
setlocal
set "TOOL_DIR=%~dp0"
set "TOOL_DLL=%TOOL_DIR%bin\QA-0.4.0\QaTelemetry.dll"
set "CASE=%TEMP%\ds1tr-ceaseless-certified-v2-lord-souls-off-05634a7e78b443e6910b317721e86b50\sweep-1-certified-lord-souls-off-1\qa-telemetry\case-runtime.json"
set "DOTNET=C:\Users\Adam\dn8sdk\dotnet.exe"

title DS1 Triple Randomizer - isolated Ceaseless QA
echo Ceaseless exact-build telemetry
echo This launcher never starts Steam and launches only the locked isolated QA executable.
echo.

if not exist "%TOOL_DLL%" (
  echo BLOCKED: telemetry helper has not been built.
  echo Build: dotnet build tools/qa-telemetry/QaTelemetry.csproj --configuration Release --output tools/qa-telemetry/bin/QA-0.4.0
  goto :failed
)
if not exist "%CASE%" (
  echo BLOCKED: selected external case descriptor is missing:
  echo %CASE%
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

echo [1/4] Verify exact package identity
"%DOTNET%" "%TOOL_DLL%" verify-case --case "%CASE%"
if errorlevel 1 goto :failed

echo.
echo [2/4] Verify isolated runtime identity
"%DOTNET%" "%TOOL_DLL%" preflight --case "%CASE%"
if errorlevel 1 goto :failed

echo.
echo [3/5] Audit the locked executable and unique signature anchors
"%DOTNET%" "%TOOL_DLL%" audit-build
if errorlevel 1 goto :failed

echo.
echo [4/5] Verify same-user Steam, verify runtime DLLs, provision or verify the QA-only AppID sidecar, and launch the isolated executable
"%DOTNET%" "%TOOL_DLL%" launch-qa --case "%CASE%"
if errorlevel 1 goto :failed

echo.
echo [5/5] Select telemetry operation after the launch path has been verified
if "%~1"=="" (
  echo No marker profile argument supplied; starting guided owner marker validation.
  echo Load the selected QA world in the already verified isolated game when prompted.
  "%DOTNET%" "%TOOL_DLL%" marker-guide --case "%CASE%"
) else (
  echo Using supplied marker profile for exact-build trace recording.
  echo Load the selected QA world in the already verified isolated game, then press a key here.
  pause >nul
  "%DOTNET%" "%TOOL_DLL%" record --case "%CASE%" --profile "%~1"
)
if errorlevel 1 goto :failed

echo.
echo Telemetry operation finished. Keep the generated profile/trace under Temp for handoff.
pause
exit /b 0

:failed
echo.
echo BLOCKED or incomplete. Marker validation cannot continue unless launch-qa reported the exact isolated process path.
echo The live telemetry reader uses query/read-only rights; the launch check uses query-limited identity reads only.
pause
exit /b 1
