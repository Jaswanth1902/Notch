@echo off
setlocal
cd /d "%~dp0"
echo [1/3] Checking Notch Hook Binary...
if not exist "bin\notch-hook.exe" (
    echo Compiling notch-hook...
    powershell -ExecutionPolicy Bypass -File .\build.ps1
)
echo [2/3] Creating Notch State Directory...
if not exist "%USERPROFILE%\.notch" mkdir "%USERPROFILE%\.notch"
echo [3/3] Launching Notch Dynamic Island HUD...
start "" powershell -WindowStyle Hidden -ExecutionPolicy Bypass -NoProfile -File ".\NativeHUD.ps1"
echo Notch HUD started successfully.
endlocal
