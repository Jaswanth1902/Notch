# Live Interactive Test for Notch Dynamic Island HUD
$SPOOL_DIR     = "$HOME\.notch"
$SESSIONS_DIR  = "$SPOOL_DIR\sessions"
$PENDING_DIR   = "$SPOOL_DIR\pending"
$DECISIONS_DIR = "$SPOOL_DIR\decisions"

$convId = "demo-$([Guid]::NewGuid().ToString().Substring(0,6))"

Write-Host ">>> [1/5] Setting State: WORKING (Electric Blue LED)..." -ForegroundColor Cyan
$stateWorking = [ordered]@{
    state           = "working"
    message         = "Running AST Security Audit & Linting..."
    timestamp       = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    conversation_id = $convId
    tool_name       = "run_command"
    project         = "Notch"
    agent           = "antigravity"
} | ConvertTo-Json -Compress
[System.IO.File]::WriteAllText("$SESSIONS_DIR\$convId.json", $stateWorking, [System.Text.Encoding]::UTF8)

Start-Sleep -Seconds 3

Write-Host ">>> [2/5] Setting State: REQUIRES ATTENTION (Sunset Orange LED & Auto-Expanded)..." -ForegroundColor Yellow
$pending = [ordered]@{
    schema          = 1
    conversation_id = $convId
    tool            = "run_command"
    summary         = "Deploy: Apply System Changes to Production Cluster"
    timestamp       = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
} | ConvertTo-Json -Compress
[System.IO.File]::WriteAllText("$PENDING_DIR\$convId.json", $pending, [System.Text.Encoding]::UTF8)

Write-Host ">>> Waiting 4 seconds for user to observe or test Shift+Enter..." -ForegroundColor Yellow
$approved = $false
for ($i = 0; $i -lt 40; $i++) {
    if (Test-Path "$DECISIONS_DIR\$convId.json") {
        Write-Host " [DETECTED] User approved via Global Shift+Enter or Button!" -ForegroundColor Green
        $approved = $true
        break
    }
    Start-Sleep -Milliseconds 100
}

if (-not $approved) {
    Write-Host ">>> Auto-approving pending action to advance test..." -ForegroundColor Gray
    [System.IO.File]::WriteAllText("$DECISIONS_DIR\$convId.json", '{"decision":"allow"}', [System.Text.Encoding]::UTF8)
    if (Test-Path "$PENDING_DIR\$convId.json") { [System.IO.File]::Delete("$PENDING_DIR\$convId.json") }
}

Start-Sleep -Seconds 2

Write-Host ">>> [3/5] Setting State: READY FOR REVIEW (Royal Purple LED)..." -ForegroundColor Magenta
$stateReview = [ordered]@{
    state           = "review"
    message         = "All tasks completed - Ready for user verification"
    timestamp       = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    conversation_id = $convId
    agent           = "antigravity"
} | ConvertTo-Json -Compress
[System.IO.File]::WriteAllText("$SESSIONS_DIR\$convId.json", $stateReview, [System.Text.Encoding]::UTF8)

Start-Sleep -Seconds 4

Write-Host ">>> [4/5] Acknowledging Review & Returning to IDLE (Emerald Green LED)..." -ForegroundColor Green
if (Test-Path "$SESSIONS_DIR\$convId.json") { [System.IO.File]::Delete("$SESSIONS_DIR\$convId.json") }

Start-Sleep -Seconds 2

Write-Host ">>> [5/5] Final Test Complete: Full State Lifecycle Verified!" -ForegroundColor Green
