[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BackupPackage,
    [string]$ServerInstance = "localhost\SQLEXPRESS",
    [string]$Database = "QrOrderDb",
    [string]$WebRoot,
    [Parameter(Mandatory = $true)]
    [switch]$ConfirmDatabaseReplacement
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

if (-not $ConfirmDatabaseReplacement) {
    throw "Restore replaces the current database. Run again with -ConfirmDatabaseReplacement after stopping the application."
}
if ($Database -notmatch '^[A-Za-z0-9_-]+$') {
    throw "Database name contains unsupported characters."
}
if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    throw "sqlcmd was not found. Install SQL Server command-line tools."
}

if ([string]::IsNullOrWhiteSpace($WebRoot)) {
    $WebRoot = Join-Path $PSScriptRoot "..\..\src\QrOrder.Web\wwwroot"
}

$packagePath = [IO.Path]::GetFullPath($BackupPackage)
$manifestPath = Join-Path $packagePath "manifest.json"
$checksumPath = Join-Path $packagePath "checksums.sha256"
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $checksumPath -PathType Leaf)) {
    throw "Backup package is missing manifest.json or checksums.sha256."
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$databaseBackupPath = Join-Path $packagePath $manifest.databaseBackup
$uploadsArchivePath = Join-Path $packagePath $manifest.uploadsArchive

foreach ($line in Get-Content -LiteralPath $checksumPath) {
    if ($line -notmatch '^([a-fA-F0-9]{64}) \*(.+)$') {
        throw "Invalid checksum entry: $line"
    }

    $expectedHash = $Matches[1].ToLowerInvariant()
    $filePath = Join-Path $packagePath $Matches[2]
    $actualHash = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $expectedHash) {
        throw "Checksum verification failed for $($Matches[2])."
    }
}

$escapedDatabase = $Database.Replace(']', ']]')
$escapedBackupPath = $databaseBackupPath.Replace("'", "''")
$restoreSql = @"
RESTORE VERIFYONLY FROM DISK = N'$escapedBackupPath' WITH CHECKSUM;
ALTER DATABASE [$escapedDatabase] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
RESTORE DATABASE [$escapedDatabase]
FROM DISK = N'$escapedBackupPath'
WITH REPLACE, CHECKSUM, RECOVERY, STATS = 10;
ALTER DATABASE [$escapedDatabase] SET MULTI_USER;
"@

try {
    & sqlcmd -S $ServerInstance -E -C -b -Q $restoreSql
    if ($LASTEXITCODE -ne 0) {
        throw "Database restore failed with exit code $LASTEXITCODE."
    }
}
catch {
    & sqlcmd -S $ServerInstance -E -C -Q "IF DB_ID(N'$($Database.Replace("'", "''"))') IS NOT NULL ALTER DATABASE [$escapedDatabase] SET MULTI_USER;" | Out-Null
    throw
}

$webRootPath = [IO.Path]::GetFullPath($WebRoot)
$uploadsPath = Join-Path $webRootPath "uploads"
$safetyArchive = Join-Path $packagePath ("uploads-before-restore-{0}.zip" -f (Get-Date -Format "yyyyMMdd-HHmmss"))

if (Test-Path -LiteralPath $uploadsPath -PathType Container) {
    [IO.Compression.ZipFile]::CreateFromDirectory(
        $uploadsPath,
        $safetyArchive,
        [IO.Compression.CompressionLevel]::Optimal,
        $false)
    Remove-Item -LiteralPath $uploadsPath -Recurse -Force
}

New-Item -ItemType Directory -Path $uploadsPath -Force | Out-Null
Expand-Archive -LiteralPath $uploadsArchivePath -DestinationPath $uploadsPath -Force

Write-Output "Restore completed. Restart the application and open /health."
Write-Output "Previous uploads safety archive: $safetyArchive"
