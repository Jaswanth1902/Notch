# Build Notch 2.0 Native Binaries and Run Invariant Verification
$cscPath = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $cscPath)) {
    Write-Host "CSC not found at $cscPath" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path "bin")) { New-Item -ItemType Directory -Path "bin" | Out-Null }

Write-Host "Compiling notch-hook.cs..." -ForegroundColor Cyan
& $cscPath /target:exe /out:bin\notch-hook.exe /optimize+ notch-hook.cs
if ($LASTEXITCODE -ne 0) {
    Write-Host "Hook compilation failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "Compiling notch-app.exe..." -ForegroundColor Cyan
& $cscPath /target:winexe /out:bin\notch-app.exe /optimize+ `
    /r:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationFramework.dll `
    /r:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationCore.dll `
    /r:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\WindowsBase.dll `
    /r:System.Xaml.dll /r:System.dll /r:System.Core.dll `
    src\NotchApp.cs

if ($LASTEXITCODE -ne 0) {
    Write-Host "App compilation failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "Binaries compiled successfully. Running regression test suite..." -ForegroundColor Green
python tests\test_auto_allow_invariants.py
if ($LASTEXITCODE -eq 0) {
    Write-Host "ALL INVARIANTS VERIFIED." -ForegroundColor Green
} else {
    Write-Host "INVARIANT REGRESSION DETECTED." -ForegroundColor Red
    exit $LASTEXITCODE
}
