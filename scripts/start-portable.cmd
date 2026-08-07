@echo off
setlocal
cd /d "%~dp0"

REM Portable mode: app data under .\data beside the exe
set ASPNETCORE_ENVIRONMENT=
set DOTNET_ENVIRONMENT=Production

REM Prefer network.json listen URL when ASPNETCORE_URLS is unset.
REM Override here if needed:
REM set ASPNETCORE_URLS=http://127.0.0.1:5180

echo Starting Jotdex (portable)...
echo Open http://127.0.0.1:5180  (or the port from Settings → Network after restart)
echo.
"Jotdex.Server.exe"
endlocal
