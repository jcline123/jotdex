@echo off
setlocal
cd /d "%~dp0"

echo.
echo  Jotdex setup
echo  ------------
echo  This runs the guided installer (scripts\Setup-Jotdex.ps1).
echo  You will be asked before anything is installed.
echo.

REM Bypass applies only to this script run — it does not change your PC's execution policy permanently.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Setup-Jotdex.ps1" %*
set EXITCODE=%ERRORLEVEL%

echo.
if %EXITCODE% neq 0 (
  echo Setup finished with errors ^(exit %EXITCODE%^).
  pause
  exit /b %EXITCODE%
)

echo Setup finished.
pause
endlocal
