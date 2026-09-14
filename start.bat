@echo off
setlocal
cd /d "%~dp0"
echo [1/3] Checking Notch 2.0 Native Binaries...
if not exist "bin" mkdir "bin"

if not exist "bin\notch-app.exe" (
    echo Compiling notch-app.exe with native CSC...
    C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /out:bin\notch-app.exe /optimize+ /r:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationFramework.dll /r:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationCore.dll /r:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\WindowsBase.dll /r:System.Xaml.dll /r:System.dll /r:System.Core.dll src\NotchApp.cs
)
if not exist "bin\notch-hook.exe" (
    echo Compiling notch-hook.exe with native CSC...
    C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:exe /out:bin\notch-hook.exe /optimize+ notch-hook.cs
)

echo [2/3] Initializing Notch Spool and IPC Channels...
if not exist "%USERPROFILE%\.notch" mkdir "%USERPROFILE%\.notch"

echo [3/3] Launching Notch 2.0 Dynamic Island Floating HUD...
start "" "%~dp0bin\notch-app.exe"
echo Notch 2.0 Dynamic Island is now floating at the top of your screen.
endlocal
