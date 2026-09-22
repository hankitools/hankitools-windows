@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0BUILD-WINDOWS.ps1"
set "hankiBuildExit=%errorlevel%"
if not "%hankiBuildExit%"=="0" (
  echo.
  echo Build did not complete. Review the message above.
)
pause
exit /b %hankiBuildExit%
