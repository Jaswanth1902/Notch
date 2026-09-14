@echo off
setlocal
cd /d "%~dp0"

echo ===================================================================
echo   NOTCH 2.0 - PHYSICAL DESKTOP DEMO SHOWCASE (winsta0\Default)
echo ===================================================================
echo 1. Launching Notch HUD floating window...
start "" "%~dp0start.bat"

echo 2. Waiting 3 seconds for HUD to dock to top screen edge...
timeout /t 3 /nobreak >nul

echo 3. Starting OpenScreen / ScreenToGif recording now!
echo    Executing the 4-stage lifecycle demonstration...
echo -------------------------------------------------------------------
powershell -ExecutionPolicy Bypass -File "%~dp0test_demo.ps1"

echo ===================================================================
echo   Showcase Complete! Notch HUD is now in standby.
echo ===================================================================
pause
