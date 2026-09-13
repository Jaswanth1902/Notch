$ErrorActionPreference = 'SilentlyContinue'

$raw = $input | Out-String

try {
    $SPOOL_DIR = "$HOME\.notch"
    $SESSIONS_DIR = "$SPOOL_DIR\sessions"
    if (-not (Test-Path $SESSIONS_DIR)) { New-Item -ItemType Directory -Path $SESSIONS_DIR -Force | Out-Null }

    if ([string]::IsNullOrWhiteSpace($raw)) { Write-Output "{}"; exit 0 }

    $j = $raw | ConvertFrom-Json
    $conv_id = if ($j.conversationId) { $j.conversationId } else { "default" }
    $project_path = Get-Location
    if ($j.workspacePaths -and $j.workspacePaths.Count -gt 0) { $project_path = $j.workspacePaths[0] }
    $project = [System.IO.Path]::GetFileName($project_path)

    $state = [ordered]@{
        state           = "thinking"
        message         = "Thinking..."
        timestamp       = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
        conversation_id = $conv_id
        project         = $project
        model           = $j.modelName
        agent           = "antigravity"
    } | ConvertTo-Json -Depth 5 -Compress

    [System.IO.File]::WriteAllText("$SESSIONS_DIR\$conv_id.json", $state, [System.Text.Encoding]::UTF8)
} catch { }

Write-Output "{}"
exit 0
