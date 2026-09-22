[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9A-Za-z.-]+$')]
    [string]$ServerAddress,
    [ValidateRange(1, 65535)]
    [int]$Port = 5080,
    [ValidatePattern('^[0-9A-Za-z][0-9A-Za-z._-]*$')]
    [string]$AppVersion = '0.1.0'
)

$ErrorActionPreference = 'Stop'
$baseUrl = "http://${ServerAddress}:$Port"
$healthUrl = "$baseUrl/health"
$versionUrl = "$baseUrl/patches/PC/$AppVersion/DefaultPackage.version"

Write-Host "Checking TCP ${ServerAddress}:$Port ..."
$connection = Test-NetConnection -ComputerName $ServerAddress -Port $Port -WarningAction SilentlyContinue
if (-not $connection.TcpTestSucceeded) {
    throw "Cannot connect to TCP ${ServerAddress}:$Port. Confirm both computers share a private LAN, Gateway is running, and the firewall rule exists."
}

Write-Host "Checking $healthUrl ..."
$health = Invoke-RestMethod -Uri $healthUrl -Method Get -TimeoutSec 10
if ($health.status -ne 'ok' -or $health.patchHosting -ne $true) {
    throw "Gateway health check failed: status=$($health.status), patchHosting=$($health.patchHosting)"
}

Write-Host "Checking $versionUrl ..."
$response = Invoke-WebRequest -Uri $versionUrl -Method Get -TimeoutSec 10
if ($response.Content -is [byte[]]) {
    $version = [Text.Encoding]::UTF8.GetString($response.Content).Trim()
}
else {
    $version = ([string]$response.Content).Trim()
}
if ([string]::IsNullOrWhiteSpace($version)) {
    throw 'The patch version file exists but is empty.'
}

[pscustomobject]@{
    Tcp5080 = 'ok'
    Gateway = $health.status
    PatchHosting = $health.patchHosting
    PublishedVersion = $version
    VersionUrl = $versionUrl
}
