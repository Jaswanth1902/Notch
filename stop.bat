@echo off
echo Stopping Notch HUD and hooks...
taskkill /F /IM powershell.exe /FI "WINDOWTITLE eq Notch Dynamic Island HUD*" >nul 2>&1
taskkill /F /IM notch-hook.exe >nul 2>&1
echo Notch HUD stopped.
