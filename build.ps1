# Build Notch Hook Binary using .NET Framework CSC or .NET SDK
$cscPath = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (Test-Path $cscPath) {
    Write-Host "Compiling notch-hook.cs with CSC..." -ForegroundColor Cyan
    & $cscPath /target:exe /out:bin\notch-hook.exe /optimize+ notch-hook.cs
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Compilation successful: bin\notch-hook.exe" -ForegroundColor Green
    } else {
        Write-Host "Compilation failed with code $LASTEXITCODE" -ForegroundColor Red
    }
} else {
    Write-Host "CSC not found at $cscPath. Please install .NET Framework or SDK." -ForegroundColor Red
}
