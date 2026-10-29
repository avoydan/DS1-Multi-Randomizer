@echo off
setlocal
set "TOOL_DIR=%~dp0"
set "TOOL_DLL=%TOOL_DIR%bin\Release\net8.0\QaTelemetry.dll"
set "CASE=%TEMP%\ds1tr-ceaseless-certified-v2-lord-souls-off-05634a7e78b443e6910b317721e86b50\sweep-1-certified-lord-souls-off-1\qa-telemetry\case-runtime.json"
set "DOTNET=C:\Users\Adam\dn8sdk\dotnet.exe"

echo Ceaseless single-case QA preflight
echo Preflight only: this launcher never starts or attaches to a game.
echo.

if not exist "%TOOL_DLL%" (
  echo BLOCKED: QA helper has not been built.
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

"%DOTNET%" "%TOOL_DLL%" preflight --case "%CASE%"
if errorlevel 1 goto :failed

echo Package/runtime preflight passed. The game was not launched or opened.
echo Live telemetry remains UNVERIFIED and is disabled in this build.
pause
exit /b 0

:failed
echo.
echo No game was launched and no process was opened.
echo Give the error and case descriptor path to the project owner before retrying.
pause
exit /b 1
