$ErrorActionPreference = 'SilentlyContinue'

$raw = $input | Out-String

try {
    $SPOOL_DIR = "$HOME\.notch"
    $SESSIONS_DIR = "$SPOOL_DIR\sessions"
    if (-not (Test-Path $SESSIONS_DIR)) { New-Item -ItemType Directory -Path $SESSIONS_DIR -Force | Out-Null }

    if ([string]::IsNullOrWhiteSpace($raw)) { Write-Output "{}"; exit 0 }

    $j = $raw | ConvertFrom-Json
    $conv_id = if ($j.conversationId) { $j.conversationId } else { "default" }

    $state = [ordered]@{
        state           = "review"
        message         = "Task completed - Ready for review"
        timestamp       = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
        conversation_id = $conv_id
        agent           = "antigravity"
    } | ConvertTo-Json -Depth 5 -Compress

    [System.IO.File]::WriteAllText("$SESSIONS_DIR\$conv_id.json", $state, [System.Text.Encoding]::UTF8)
} catch { }

Write-Output "{}"
exit 0
