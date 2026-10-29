@echo off
setlocal
set "TOOL_DIR=%~dp0"
set "TOOL_DLL=%TOOL_DIR%bin\Release\net8.0\QaTelemetry.dll"
set "CASE=%TEMP%\ds1tr-ceaseless-certified-v2-lord-souls-off-05634a7e78b443e6910b317721e86b50\sweep-1-certified-lord-souls-off-1\qa-telemetry\case-runtime.json"
set "QA_EXE=C:\Users\Adam\ds1tr-qa\runtime\DARK SOULS REMASTERED - CEASELESS QA\DarkSoulsRemastered.exe"
set "DOTNET=C:\Users\Adam\dn8sdk\dotnet.exe"

title DS1 Triple Randomizer - isolated Ceaseless QA
echo Ceaseless exact-build telemetry
echo This launcher never starts Steam or launches the game.
echo.

if not exist "%TOOL_DLL%" (
  echo BLOCKED: telemetry helper has not been built.
  echo Build: dotnet build tools/qa-telemetry/QaTelemetry.csproj --configuration Release
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
echo [3/4] Audit the locked executable and unique signature anchors
"%DOTNET%" "%TOOL_DLL%" audit-build
if errorlevel 1 goto :failed

echo.
echo [4/4] Select telemetry operation
if "%~1"=="" (
  echo No marker profile argument supplied; starting guided owner marker validation.
  echo When asked, launch ONLY:
  echo %QA_EXE%
  "%DOTNET%" "%TOOL_DLL%" marker-guide --case "%CASE%"
) else (
  echo Using supplied marker profile for exact-build trace recording.
  echo Ensure the isolated QA game is already running with the selected package.
  "%DOTNET%" "%TOOL_DLL%" record --case "%CASE%" --profile "%~1"
)
if errorlevel 1 goto :failed

echo.
echo Telemetry operation finished. Keep the generated profile/trace under Temp for handoff.
pause
exit /b 0

:failed
echo.
echo BLOCKED or incomplete. No game was launched by this script.
echo The tool may attach only to the exact locked isolated QA executable, and only with query/read rights.
pause
exit /b 1
