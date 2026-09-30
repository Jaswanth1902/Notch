# Build Notch 2.0 Native Binaries and Run Invariant Verification
$cscPath = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $cscPath)) {
    Write-Host "CSC not found at $cscPath" -ForegroundColor Red
    exit 1
}

$root = $PSScriptRoot
if (-not $root) { $root = Get-Location }

$binDir = Join-Path $root "bin"
if (-not (Test-Path $binDir)) { New-Item -ItemType Directory -Path $binDir | Out-Null }

Write-Host "Compiling notch-hook.cs..." -ForegroundColor Cyan
$outHook = "/out:" + (Join-Path $binDir "notch-hook.exe")
$srcHook = Join-Path $root "notch-hook.cs"
& $cscPath /target:exe $outHook /optimize+ $srcHook
if ($LASTEXITCODE -ne 0) {
    Write-Host "Hook compilation failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "Compiling notch-relay.exe..." -ForegroundColor Cyan
$outRelay = "/out:" + (Join-Path $binDir "notch-relay.exe")
$srcRelay = Join-Path $root "src\NotchRelay.cs"
& $cscPath /target:exe $outRelay /optimize+ $srcRelay
if ($LASTEXITCODE -ne 0) {
    Write-Host "Relay compilation failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "Compiling notch-core.exe..." -ForegroundColor Cyan
$outCore = "/out:" + (Join-Path $binDir "notch-core.exe")
$srcCore = Join-Path $root "src\NotchCore.cs"
& $cscPath /target:exe $outCore /optimize+ $srcCore
if ($LASTEXITCODE -ne 0) {
    Write-Host "NotchCore compilation failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "Binaries compiled successfully. Running regression test suite..." -ForegroundColor Green
$testFile = Join-Path $root "tests\test_coucou_notch_integration.py"
python -m pytest $testFile -v
if ($LASTEXITCODE -eq 0) {
    Write-Host "ALL COUCOU & NOTCH INVARIANTS VERIFIED." -ForegroundColor Green
} else {
    Write-Host "INVARIANT REGRESSION DETECTED." -ForegroundColor Red
    exit $LASTEXITCODE
}
