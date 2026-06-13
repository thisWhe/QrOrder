[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BackupPackage,
    [string]$ServerInstance = "localhost\SQLEXPRESS",
    [string]$TestDatabase = "QrOrderRestoreTest",
    [switch]$KeepTestDatabase
)

$ErrorActionPreference = "Stop"

function Invoke-Sql {
    param([string]$Query, [string]$Database = "master")
    $output = & sqlcmd -S $ServerInstance -E -b -W -h -1 -s "|" -d $Database -Q $Query
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed with exit code $LASTEXITCODE." }
    return $output
}

function Get-FirstOutputLine {
    param([object]$Value, [string]$Description)
    $line = @($Value) |
        ForEach-Object { [string]$_ } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($line)) { throw "$Description could not be read from SQL Server." }
    return $line.Trim()
}

if ($TestDatabase -notmatch '^[A-Za-z0-9_-]+$' -or $TestDatabase -notmatch '(?i)test') {
    throw "TestDatabase must be a safe name containing 'test'."
}
if ($TestDatabase -ieq "QrOrderDb") { throw "The live QrOrderDb database cannot be used as the restore test target." }
if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) { throw "sqlcmd was not found." }

$packagePath = [IO.Path]::GetFullPath($BackupPackage)
$manifestPath = Join-Path $packagePath "manifest.json"
$checksumPath = Join-Path $packagePath "checksums.sha256"
if (-not (Test-Path $manifestPath -PathType Leaf) -or -not (Test-Path $checksumPath -PathType Leaf)) {
    throw "Backup package is missing manifest.json or checksums.sha256."
}

$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$backupPath = Join-Path $packagePath $manifest.databaseBackup
foreach ($line in Get-Content $checksumPath) {
    if ($line -notmatch '^([a-fA-F0-9]{64}) \*(.+)$') { throw "Invalid checksum entry: $line" }
    $actual = (Get-FileHash (Join-Path $packagePath $Matches[2]) -Algorithm SHA256).Hash
    if ($actual -ine $Matches[1]) { throw "Checksum verification failed for $($Matches[2])." }
}

$escapedBackup = $backupPath.Replace("'", "''")
$escapedDatabase = $TestDatabase.Replace(']', ']]')
$sqlDatabaseName = $TestDatabase.Replace("'", "''")

Write-Host "Backup checksum and SQL backup structure are being verified." -ForegroundColor Cyan
Invoke-Sql "RESTORE VERIFYONLY FROM DISK=N'$escapedBackup' WITH CHECKSUM;" | Out-Host

$fileRows = Invoke-Sql "SET NOCOUNT ON; RESTORE FILELISTONLY FROM DISK=N'$escapedBackup';"
$logicalFiles = @()
foreach ($row in $fileRows) {
    $columns = $row -split '\|'
    if ($columns.Count -ge 3 -and $columns[2] -in @('D', 'L')) {
        $logicalFiles += [pscustomobject]@{ LogicalName = $columns[0].Trim(); Type = $columns[2].Trim() }
    }
}
if ($logicalFiles.Count -eq 0) { throw "Logical database files could not be read from the backup." }

$dataPath = Get-FirstOutputLine (Invoke-Sql "SET NOCOUNT ON; SELECT COALESCE(CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(4000)), LEFT(physical_name, LEN(physical_name) - CHARINDEX('\', REVERSE(physical_name)) + 1)) FROM sys.master_files WHERE database_id=1 AND file_id=1;") "Default data path"
$logPath = Get-FirstOutputLine (Invoke-Sql "SET NOCOUNT ON; SELECT COALESCE(CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(4000)), LEFT(physical_name, LEN(physical_name) - CHARINDEX('\', REVERSE(physical_name)) + 1)) FROM sys.master_files WHERE database_id=1 AND file_id=2;") "Default log path"

$moves = @()
$dataIndex = 0
$logIndex = 0
foreach ($file in $logicalFiles) {
    $logicalName = $file.LogicalName.Replace("'", "''")
    if ($file.Type -eq 'L') {
        $logIndex++
        $target = Join-Path $logPath ("{0}_{1}.ldf" -f $TestDatabase, $logIndex)
    }
    else {
        $dataIndex++
        $extension = if ($dataIndex -eq 1) { ".mdf" } else { ".ndf" }
        $target = Join-Path $dataPath ("{0}_{1}{2}" -f $TestDatabase, $dataIndex, $extension)
    }
    $moves += "MOVE N'$logicalName' TO N'$($target.Replace("'", "''"))'"
}

$dropSql = "IF DB_ID(N'$sqlDatabaseName') IS NOT NULL BEGIN ALTER DATABASE [$escapedDatabase] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$escapedDatabase]; END;"
try {
    Invoke-Sql $dropSql | Out-Null
    $restoreSql = "RESTORE DATABASE [$escapedDatabase] FROM DISK=N'$escapedBackup' WITH CHECKSUM, RECOVERY, STATS=10, $($moves -join ', ');"
    Invoke-Sql $restoreSql | Out-Host

    $validation = Invoke-Sql "SET NOCOUNT ON; SELECT CONCAT((SELECT COUNT(*) FROM dbo.Tenants), '|', (SELECT COUNT(*) FROM dbo.Products), '|', (SELECT COUNT(*) FROM dbo.Orders));" $TestDatabase
    $validationLine = Get-FirstOutputLine $validation "Restore validation result"
    Write-Host "Restore test succeeded. Tenant|Product|Order counts: $validationLine" -ForegroundColor Green
}
finally {
    if (-not $KeepTestDatabase) {
        Invoke-Sql $dropSql | Out-Null
        Write-Host "Temporary test database was removed."
    }
    else {
        Write-Warning "Temporary database was kept: $TestDatabase"
    }
}
