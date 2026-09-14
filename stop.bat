@echo off
echo Stopping Notch 2.0 Dynamic Island and hooks...
taskkill /F /IM notch-app.exe >nul 2>&1
taskkill /F /IM notch-core.exe >nul 2>&1
taskkill /F /IM notch-hook.exe >nul 2>&1
taskkill /F /IM powershell.exe /FI "WINDOWTITLE eq Notch Dynamic Island HUD*" >nul 2>&1
echo Notch 2.0 stopped cleanly.
