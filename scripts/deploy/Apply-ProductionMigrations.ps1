[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$MigrationBundle,
    [Parameter(Mandatory = $true)]
    [string]$ConnectionString
)

$ErrorActionPreference = "Stop"
$bundlePath = [IO.Path]::GetFullPath($MigrationBundle)
if (-not (Test-Path -LiteralPath $bundlePath -PathType Leaf)) {
    throw "Migration bundle was not found: $bundlePath"
}

$previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
$previousConnection = $env:ConnectionStrings__Default

try {
    $env:ASPNETCORE_ENVIRONMENT = "Production"
    $env:ConnectionStrings__Default = $ConnectionString

    & $bundlePath
    if ($LASTEXITCODE -ne 0) {
        throw "Migration bundle failed with exit code $LASTEXITCODE."
    }

    Write-Output "Production migrations completed successfully."
}
finally {
    $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment
    $env:ConnectionStrings__Default = $previousConnection
}
