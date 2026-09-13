$SPOOL_DIR     = "$HOME\.notch"
$SESSIONS_DIR  = "$SPOOL_DIR\sessions"
$PENDING_DIR   = "$SPOOL_DIR\pending"
$DECISIONS_DIR = "$SPOOL_DIR\decisions"

foreach ($d in @($SPOOL_DIR, $SESSIONS_DIR, $PENDING_DIR, $DECISIONS_DIR)) {
    if (-not (Test-Path $d)) {
        New-Item -ItemType Directory -Path $d -Force -ErrorAction SilentlyContinue | Out-Null
    }
}

$utf8NoBom = New-Object System.Text.UTF8Encoding $false

function Write-JsonAtomic([string]$filePath, [string]$content) {
    try {
        [System.IO.File]::WriteAllText($filePath, $content, $utf8NoBom)
    } catch {
        # Fallback to Set-Content if needed
        Set-Content -Path $filePath -Value $content -Encoding UTF8 -Force
    }
}
