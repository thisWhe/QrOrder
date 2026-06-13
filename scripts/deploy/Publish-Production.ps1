[CmdletBinding()]
param(
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = (Join-Path (Get-Location) "artifacts\production"),
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$outputRootPath = [IO.Path]::GetFullPath($OutputRoot)
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$packageRoot = Join-Path $outputRootPath "QrOrder_$timestamp"
$appPath = Join-Path $packageRoot "app"
$databasePath = Join-Path $packageRoot "database"

New-Item -ItemType Directory -Path $appPath -Force | Out-Null
New-Item -ItemType Directory -Path $databasePath -Force | Out-Null

Push-Location $repositoryRoot
try {
    dotnet restore QrOrder.sln -r $Runtime
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed." }

    dotnet build QrOrder.sln -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Release build failed." }

    $selfContainedValue = if ($SelfContained) { "true" } else { "false" }
    dotnet publish src\QrOrder.Web\QrOrder.Web.csproj `
        -c Release `
        -r $Runtime `
        --self-contained $selfContainedValue `
        --no-restore `
        -o $appPath
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

    dotnet ef migrations bundle `
        --project src\QrOrder.Infrastructure\QrOrder.Infrastructure.csproj `
        --startup-project src\QrOrder.Web\QrOrder.Web.csproj `
        --context AppDbContext `
        --configuration Release `
        --runtime $Runtime `
        --self-contained `
        --force `
        --output (Join-Path $databasePath "QrOrder.Migrations.exe")
    if ($LASTEXITCODE -ne 0) { throw "EF migration bundle creation failed." }

    Copy-Item deploy\production.env.example (Join-Path $packageRoot "production.env.example")
    Copy-Item docs\PRODUCTION-KURULUM.md (Join-Path $packageRoot "PRODUCTION-KURULUM.md")
    Copy-Item docs\YEDEKLEME-VE-GERI-YUKLEME.md (Join-Path $packageRoot "YEDEKLEME-VE-GERI-YUKLEME.md")
    Copy-Item docs\PILOT-KABUL-VE-TESLIM.md (Join-Path $packageRoot "PILOT-KABUL-VE-TESLIM.md")
    Copy-Item docs\PILOT-ISLETME-KURULUMU.md (Join-Path $packageRoot "PILOT-ISLETME-KURULUMU.md")
    Copy-Item docs\PILOT-GIZLILIK-VE-KVKK-TASLAGI.md (Join-Path $packageRoot "PILOT-GIZLILIK-VE-KVKK-TASLAGI.md")

    $operationsPath = Join-Path $packageRoot "operations"
    New-Item -ItemType Directory -Path $operationsPath -Force | Out-Null
    Copy-Item scripts\backup\Backup-QrOrder.ps1 $operationsPath
    Copy-Item scripts\backup\Restore-QrOrder.ps1 $operationsPath
    Copy-Item scripts\backup\Test-QrOrderBackupRestore.ps1 $operationsPath

    $zipPath = "$packageRoot.zip"
    Compress-Archive -LiteralPath $packageRoot -DestinationPath $zipPath -CompressionLevel Optimal

    Write-Output "Production package: $packageRoot"
    Write-Output "Production archive: $zipPath"
}
catch {
    if (Test-Path -LiteralPath $packageRoot) {
        Remove-Item -LiteralPath $packageRoot -Recurse -Force
    }
    throw
}
finally {
    Pop-Location
}
