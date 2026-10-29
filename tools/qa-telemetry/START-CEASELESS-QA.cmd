@echo off
setlocal
set "TOOL_DIR=%~dp0"
set "TOOL_DLL=%TOOL_DIR%bin\QA-0.5.0\QaTelemetry.dll"
set "CASE=%TEMP%\ds1tr-ceaseless-certified-v2-lord-souls-off-05634a7e78b443e6910b317721e86b50\sweep-1-certified-lord-souls-off-1\qa-telemetry\case-runtime.json"
set "DOTNET=C:\Users\Adam\dn8sdk\dotnet.exe"

title DS1 Triple Randomizer - isolated Ceaseless QA
echo Ceaseless exact-build telemetry
echo This launcher never starts Steam and launches only the locked package-applied QA runtime.
echo.

if "%~1"=="" (
  echo BLOCKED: a VERIFIED executable telemetry profile is required before the Ceaseless session.
  echo First run VALIDATE-TELEMETRY-MARKERS.cmd on the clean vanilla QA runtime.
  echo Then pass the emitted profile path as the first argument to this launcher.
  goto :failed
)
if not "%~2"=="" (
  echo BLOCKED: pass exactly one verified executable telemetry profile path.
  goto :failed
)

if not exist "%TOOL_DLL%" (
  echo BLOCKED: telemetry helper has not been built.
  echo Build: dotnet build tools/qa-telemetry/QaTelemetry.csproj --configuration Release --output tools/qa-telemetry/bin/QA-0.5.0
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

echo [1/6] Verify executable telemetry profile from clean vanilla calibration
"%DOTNET%" "%TOOL_DLL%" verify-profile --profile "%~1"
if errorlevel 1 goto :failed

echo.
echo [2/6] Verify exact Ceaseless package identity
"%DOTNET%" "%TOOL_DLL%" verify-case --case "%CASE%"
if errorlevel 1 goto :failed

echo.
echo [3/6] Verify isolated package-applied runtime identity
"%DOTNET%" "%TOOL_DLL%" preflight --case "%CASE%"
if errorlevel 1 goto :failed

echo.
echo [4/6] Audit the locked package-applied executable and telemetry anchors
"%DOTNET%" "%TOOL_DLL%" audit-build --target ceaseless
if errorlevel 1 goto :failed

echo.
echo [5/6] Verify same-user Steam, runtime DLLs, QA-only AppID sidecar, and launch only the package-applied QA executable
"%DOTNET%" "%TOOL_DLL%" launch-qa --case "%CASE%"
if errorlevel 1 goto :failed

echo.
echo [6/6] Record the selected Ceaseless gameplay case using the separately verified executable telemetry profile.
echo Load the certified Ceaseless world in the verified package-applied QA game, then press a key here.
pause >nul
"%DOTNET%" "%TOOL_DLL%" record --case "%CASE%" --profile "%~1"
if errorlevel 1 goto :failed

echo.
echo Telemetry operation finished. Keep the generated profile/trace under Temp for handoff.
pause
exit /b 0

:failed
echo.
echo BLOCKED or incomplete. Ceaseless recording requires a verified executable profile and the exact package-applied process path.
echo The live telemetry reader uses query/read-only rights; the launch check uses query-limited identity reads only.
pause
exit /b 1
