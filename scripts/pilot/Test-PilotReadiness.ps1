[CmdletBinding()]
param(
    [string]$EnvironmentFile,
    [switch]$SkipBuild,
    [switch]$IncludePublish,
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))

function Assert-Condition {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Invoke-DotNet {
    param([string[]]$Arguments, [string]$FailureMessage)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw $FailureMessage }
}

function Read-EnvironmentFile {
    param([string]$Path)
    $values = @{}
    foreach ($line in Get-Content -LiteralPath $Path) {
        $trimmed = $line.Trim()
        if (-not $trimmed -or $trimmed.StartsWith("#")) { continue }
        $separator = $trimmed.IndexOf("=")
        if ($separator -lt 1) { throw "Invalid environment line: $line" }
        $values[$trimmed.Substring(0, $separator).Trim()] = $trimmed.Substring($separator + 1).Trim()
    }
    return $values
}

function Test-Placeholder {
    param([string]$Value)
    return [string]::IsNullOrWhiteSpace($Value) -or
        $Value -match 'CHANGE_ME|SQL_SERVER|YOUR_|example\.com'
}

Push-Location $repositoryRoot
try {
    Write-Host "[1/5] Required files" -ForegroundColor Cyan
    $requiredFiles = @(
        "QrOrder.sln",
        "deploy\production.env.example",
        "scripts\backup\Backup-QrOrder.ps1",
        "scripts\backup\Restore-QrOrder.ps1",
        "scripts\backup\Test-QrOrderBackupRestore.ps1",
        "scripts\security\Test-GitHubSafety.ps1",
        "docs\PRODUCTION-KURULUM.md",
        "docs\PILOT-KABUL-VE-TESLIM.md",
        "docs\PILOT-ISLETME-KURULUMU.md"
    )
    foreach ($file in $requiredFiles) {
        Assert-Condition (Test-Path -LiteralPath $file -PathType Leaf) "Required file is missing: $file"
    }
    & .\scripts\security\Test-GitHubSafety.ps1

    Write-Host "[2/5] Production defaults" -ForegroundColor Cyan
    $productionSettings = Get-Content src\QrOrder.Web\appsettings.Production.json -Raw | ConvertFrom-Json
    Assert-Condition ($productionSettings.Seed.DemoData -eq $false) "Seed:DemoData must be false in Production."
    Assert-Condition ($productionSettings.Database.ApplyMigrationsOnStartup -eq $false) "Database migrations must not run automatically in Production."

    Write-Host "[3/5] Release build and migration consistency" -ForegroundColor Cyan
    if (-not $SkipBuild) {
        Invoke-DotNet @("build", "QrOrder.sln", "-c", "Release", "--no-restore") "Release build failed. Stop the running application and retry if files are locked."
    }
    Invoke-DotNet @(
        "ef", "migrations", "has-pending-model-changes",
        "--project", "src\QrOrder.Infrastructure\QrOrder.Infrastructure.csproj",
        "--startup-project", "src\QrOrder.Web\QrOrder.Web.csproj",
        "--context", "AppDbContext",
        "--configuration", "Release",
        "--no-build"
    ) "The EF model and migrations are inconsistent."

    Write-Host "[4/5] Production environment" -ForegroundColor Cyan
    if ($EnvironmentFile) {
        $environmentPath = [IO.Path]::GetFullPath($EnvironmentFile)
        Assert-Condition (Test-Path -LiteralPath $environmentPath -PathType Leaf) "Environment file was not found."
        $envValues = Read-EnvironmentFile $environmentPath

        Assert-Condition ($envValues["ASPNETCORE_ENVIRONMENT"] -eq "Production") "ASPNETCORE_ENVIRONMENT must be Production."
        $publicUri = $null
        Assert-Condition ([Uri]::TryCreate($envValues["PublicBaseUrl"], [UriKind]::Absolute, [ref]$publicUri) -and $publicUri.Scheme -eq "https") "PublicBaseUrl must be an absolute HTTPS URL."
        $allowedHosts = $envValues["AllowedHosts"] -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ }
        Assert-Condition ($allowedHosts.Count -gt 0 -and $allowedHosts -notcontains "*") "AllowedHosts must contain the public hostname instead of '*'."
        Assert-Condition ($allowedHosts -contains $publicUri.Host) "AllowedHosts must include the PublicBaseUrl hostname."
        Assert-Condition (-not (Test-Placeholder $envValues["ConnectionStrings__Default"])) "Production database connection still contains a placeholder."
        Assert-Condition (-not (Test-Placeholder $envValues["Jwt__Key"]) -and $envValues["Jwt__Key"].Length -ge 48) "Jwt__Key must be a non-placeholder value with at least 48 characters."
        Assert-Condition ($envValues["Seed__DemoData"] -eq "false") "Seed__DemoData must be false."
        Assert-Condition ($envValues["Database__ApplyMigrationsOnStartup"] -eq "false") "Database__ApplyMigrationsOnStartup must be false."

        foreach ($pathKey in @("Storage__UploadsPath", "DataProtection__KeysPath", "Serilog__WriteTo__1__Args__path")) {
            Assert-Condition ([IO.Path]::IsPathFullyQualified($envValues[$pathKey])) "$pathKey must be an absolute path."
        }
        Write-Host "Production environment values are valid. Secret values were not printed." -ForegroundColor Green
    }
    else {
        Write-Warning "No -EnvironmentFile was supplied. Real production secrets and paths were not validated."
    }

    Write-Host "[5/5] Deployment package" -ForegroundColor Cyan
    if ($IncludePublish) {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\deploy\Publish-Production.ps1 -Runtime $Runtime
        if ($LASTEXITCODE -ne 0) { throw "Production package creation failed." }
    }
    else {
        Write-Host "Package creation skipped. Add -IncludePublish when the production environment is ready."
    }

    Write-Host "Pilot readiness checks completed successfully." -ForegroundColor Green
}
finally {
    Pop-Location
}
