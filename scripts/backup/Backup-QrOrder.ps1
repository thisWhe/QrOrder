[CmdletBinding()]
param(
    [string]$ServerInstance = "localhost\SQLEXPRESS",
    [string]$Database = "QrOrderDb",
    [Parameter(Mandatory = $true)]
    [string]$BackupRoot,
    [string]$WebRoot,
    [ValidateRange(1, 3650)]
    [int]$RetentionDays = 30,
    [switch]$DisableCompression
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Assert-SafeDatabaseName([string]$Name) {
    if ($Name -notmatch '^[A-Za-z0-9_-]+$') {
        throw "Database name contains unsupported characters."
    }
}

function Invoke-SqlCommand([string]$Query) {
    & sqlcmd -S $ServerInstance -E -C -b -Q $Query
    if ($LASTEXITCODE -ne 0) {
        throw "sqlcmd failed with exit code $LASTEXITCODE."
    }
}

function Invoke-SqlScalar([string]$Query) {
    $output = & sqlcmd -S $ServerInstance -E -C -b -h -1 -W -Q $Query
    if ($LASTEXITCODE -ne 0) {
        throw "sqlcmd failed with exit code $LASTEXITCODE."
    }

    $value = $output |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -First 1
    return [string]$value.Trim()
}

Assert-SafeDatabaseName $Database
if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    throw "sqlcmd was not found. Install SQL Server command-line tools."
}

if ([string]::IsNullOrWhiteSpace($WebRoot)) {
    $WebRoot = Join-Path $PSScriptRoot "..\..\src\QrOrder.Web\wwwroot"
}

$backupRootPath = [IO.Path]::GetFullPath($BackupRoot)
$webRootPath = [IO.Path]::GetFullPath($WebRoot)
$uploadsPath = Join-Path $webRootPath "uploads"
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$packagePath = Join-Path $backupRootPath "QrOrder_$timestamp"
$databaseBackupPath = Join-Path $packagePath "$Database.bak"
$uploadsArchivePath = Join-Path $packagePath "uploads.zip"
$sqlEdition = Invoke-SqlScalar "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('Edition') AS nvarchar(4000));"

New-Item -ItemType Directory -Path $packagePath -Force | Out-Null

$escapedDatabase = $Database.Replace(']', ']]')
$escapedBackupPath = $databaseBackupPath.Replace("'", "''")
$supportsCompression = $sqlEdition -notmatch "Express"
$compressionOption = if ($DisableCompression -or -not $supportsCompression) { "" } else { ", COMPRESSION" }

$backupSql = @"
BACKUP DATABASE [$escapedDatabase]
TO DISK = N'$escapedBackupPath'
WITH COPY_ONLY, INIT, CHECKSUM$compressionOption, STATS = 10;

RESTORE VERIFYONLY
FROM DISK = N'$escapedBackupPath'
WITH CHECKSUM;
"@

try {
    Invoke-SqlCommand $backupSql

    if (-not (Test-Path -LiteralPath $databaseBackupPath -PathType Leaf)) {
        throw "SQL Server reported success but the backup file is not readable at $databaseBackupPath."
    }

    if (Test-Path -LiteralPath $uploadsPath -PathType Container) {
        [IO.Compression.ZipFile]::CreateFromDirectory(
            $uploadsPath,
            $uploadsArchivePath,
            [IO.Compression.CompressionLevel]::Optimal,
            $false)
    }
    else {
        $emptyUploadsPath = Join-Path $packagePath "empty-uploads"
        New-Item -ItemType Directory -Path $emptyUploadsPath | Out-Null
        [IO.Compression.ZipFile]::CreateFromDirectory(
            $emptyUploadsPath,
            $uploadsArchivePath,
            [IO.Compression.CompressionLevel]::Optimal,
            $false)
        Remove-Item -LiteralPath $emptyUploadsPath -Recurse -Force
    }

    $manifest = [ordered]@{
        formatVersion = 1
        createdAtUtc = (Get-Date).ToUniversalTime().ToString("O")
        serverInstance = $ServerInstance
        database = $Database
        databaseBackup = [IO.Path]::GetFileName($databaseBackupPath)
        uploadsArchive = [IO.Path]::GetFileName($uploadsArchivePath)
    }
    $manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $packagePath "manifest.json") -Encoding UTF8

    $checksumLines = @(
        "{0} *{1}" -f (Get-FileHash -LiteralPath $databaseBackupPath -Algorithm SHA256).Hash.ToLowerInvariant(), [IO.Path]::GetFileName($databaseBackupPath)
        "{0} *{1}" -f (Get-FileHash -LiteralPath $uploadsArchivePath -Algorithm SHA256).Hash.ToLowerInvariant(), [IO.Path]::GetFileName($uploadsArchivePath)
        "{0} *manifest.json" -f (Get-FileHash -LiteralPath (Join-Path $packagePath "manifest.json") -Algorithm SHA256).Hash.ToLowerInvariant()
    )
    $checksumLines | Set-Content -LiteralPath (Join-Path $packagePath "checksums.sha256") -Encoding ASCII

    $retentionThreshold = (Get-Date).AddDays(-$RetentionDays)
    Get-ChildItem -LiteralPath $backupRootPath -Directory -Filter "QrOrder_*" |
        Where-Object { $_.FullName -ne $packagePath -and $_.LastWriteTime -lt $retentionThreshold } |
        ForEach-Object {
            $candidate = [IO.Path]::GetFullPath($_.FullName)
            if ($candidate.StartsWith($backupRootPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
                Remove-Item -LiteralPath $candidate -Recurse -Force
            }
        }

    Write-Output "Backup completed: $packagePath"
}
catch {
    if (Test-Path -LiteralPath $packagePath) {
        Remove-Item -LiteralPath $packagePath -Recurse -Force
    }
    throw
}
