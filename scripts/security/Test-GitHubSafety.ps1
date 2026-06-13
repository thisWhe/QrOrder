[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))

function Invoke-Git {
    param([string[]]$Arguments)
    $output = & git -c "safe.directory=$($repositoryRoot.Replace('\', '/'))" @Arguments
    if ($LASTEXITCODE -ne 0) { throw "git command failed: git $($Arguments -join ' ')" }
    return @($output)
}

Push-Location $repositoryRoot
try {
    $trackedFiles = Invoke-Git @("ls-files")
    $forbiddenPatterns = @(
        '(^|/)(backups|artifacts|logs|\.security-check|\.tenant-check)/',
        '(^|/)src/QrOrder\.Web/wwwroot/uploads/',
        '(^|/)appsettings\.(Development|Local)\.json$',
        '(^|/)(production\.env|secrets\.json)$',
        '\.(bak|mdf|ldf|bacpac|pfx|p12|pem|key)$'
    )

    $forbiddenTrackedFiles = foreach ($file in $trackedFiles) {
        if ($forbiddenPatterns | Where-Object { $file -match $_ }) { $file }
    }
    if ($forbiddenTrackedFiles) {
        throw "Sensitive/generated files are tracked by Git:`n$($forbiddenTrackedFiles -join "`n")"
    }

    $allowedPlaceholderFiles = @(
        "deploy/production.env.example",
        "src/QrOrder.Web/appsettings.json",
        "README.md",
        "docs/PRODUCTION-KURULUM.md",
        "scripts/security/Test-GitHubSafety.ps1"
    )
    $secretPatterns = @(
        '-----BEGIN (RSA |OPENSSH |EC )?PRIVATE KEY-----',
        '(?i)(jwt(__|:)?key|api[_-]?key|client[_-]?secret)\s*[=:]\s*["'']?(?!CHANGE_ME|\.\.\.)[^\s"'']{20,}',
        '(?i)(^|;)\s*password\s*=\s*(?!CHANGE_ME)[^;\r\n]{8,}'
    )

    $findings = @()
    foreach ($file in $trackedFiles) {
        if ($allowedPlaceholderFiles -contains $file -or -not (Test-Path -LiteralPath $file -PathType Leaf)) { continue }

        $content = Get-Content -LiteralPath $file -Raw -ErrorAction SilentlyContinue
        if ($null -eq $content) { continue }
        foreach ($pattern in $secretPatterns) {
            if ($content -match $pattern) {
                $findings += "$file matched secret pattern: $pattern"
                break
            }
        }
    }

    if ($findings) {
        throw "Possible secrets were found in tracked files:`n$($findings -join "`n")"
    }

    Write-Host "GitHub safety check passed." -ForegroundColor Green
    Write-Host "Tracked files do not include uploads, backups, logs, local settings, database files or private keys."
}
finally {
    Pop-Location
}
