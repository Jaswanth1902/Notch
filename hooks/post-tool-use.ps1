$ErrorActionPreference = 'SilentlyContinue'

$raw = $input | Out-String

try {
    $SPOOL_DIR = "$HOME\.notch"
    $SESSIONS_DIR = "$SPOOL_DIR\sessions"
    if (-not (Test-Path $SESSIONS_DIR)) { New-Item -ItemType Directory -Path $SESSIONS_DIR -Force | Out-Null }

    if ([string]::IsNullOrWhiteSpace($raw)) { Write-Output "{}"; exit 0 }

    $j = $raw | ConvertFrom-Json
    $conv_id = if ($j.conversationId) { $j.conversationId } else { "default" }
    $tool = if ($j.toolCall.name) { $j.toolCall.name } else { "tool" }
    $targs = $j.toolCall.args

    $summary = "Running $tool"
    if ($tool -eq "run_command") {
        $cmd = if ($targs.CommandLine) { [string]$targs.CommandLine } else { "" }
        if ($cmd.Length -gt 60) { $cmd = $cmd.Substring(0, 57) + "..." }
        $summary = "Ran: $cmd"
    } elseif ($tool -eq "view_file") {
        $p = if ($targs.AbsolutePath) { [string]$targs.AbsolutePath } else { "" }
        $leaf = if ($p) { [System.IO.Path]::GetFileName($p) } else { "" }
        $summary = "Read: $leaf"
    } elseif ($tool -eq "replace_file_content" -or $tool -eq "write_to_file") {
        $p = if ($targs.TargetFile) { [string]$targs.TargetFile } else { "" }
        $leaf = if ($p) { [System.IO.Path]::GetFileName($p) } else { "" }
        $summary = "Edited: $leaf"
    } elseif ($tool -eq "grep_search") {
        $q = if ($targs.Query) { [string]$targs.Query } else { "" }
        if ($q.Length -gt 40) { $q = $q.Substring(0, 37) + "..." }
        $summary = "Search: $q"
    } elseif ($tool -eq "search_web") {
        $q = if ($targs.query) { [string]$targs.query } else { "" }
        if ($q.Length -gt 40) { $q = $q.Substring(0, 37) + "..." }
        $summary = "Web: $q"
    }

    $project_path = Get-Location
    if ($j.workspacePaths -and $j.workspacePaths.Count -gt 0) { $project_path = $j.workspacePaths[0] }
    $project = [System.IO.Path]::GetFileName($project_path)

    $state = [ordered]@{
        state            = "working"
        message          = $summary
        timestamp        = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
        conversation_id  = $conv_id
        tool_name        = $tool
        project          = $project
        model            = $j.modelName
        agent            = "antigravity"
    } | ConvertTo-Json -Depth 5 -Compress

    [System.IO.File]::WriteAllText("$SESSIONS_DIR\$conv_id.json", $state, [System.Text.Encoding]::UTF8)
} catch { }

Write-Output "{}"
exit 0
