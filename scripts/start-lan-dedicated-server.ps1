[CmdletBinding()]
param(
    [string]$LogPath = 'Build\Logs\LanDedicatedServer.log'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$serverRepository = Split-Path -Parent $PSScriptRoot
$serverExecutable = Join-Path $serverRepository 'Build\WindowsServer\HavenServer.exe'
if (-not (Test-Path -LiteralPath $serverExecutable -PathType Leaf)) {
    throw 'Dedicated Server build is missing. Run Haven/Build/Windows Dedicated Server first.'
}

$configuration = Get-NetIPConfiguration -ErrorAction Stop |
    Where-Object { $_.IPv4DefaultGateway -and $_.IPv4Address -and $_.NetAdapter.Status -eq 'Up' -and $_.NetAdapter.HardwareInterface } |
    Sort-Object { $_.NetIPv4Interface.InterfaceMetric } |
    Select-Object -First 1
if (-not $configuration) { throw 'No active physical LAN adapter with an IPv4 gateway was found.' }
$profile = Get-NetConnectionProfile -InterfaceIndex $configuration.InterfaceIndex -ErrorAction Stop
if ($profile.NetworkCategory -ne 'Private') {
    throw "Network '$($profile.Name)' is $($profile.NetworkCategory). Refusing to expose the game server outside a Private LAN."
}

$resolvedLog = if ([IO.Path]::IsPathRooted($LogPath)) { [IO.Path]::GetFullPath($LogPath) } else { [IO.Path]::GetFullPath((Join-Path $serverRepository $LogPath)) }
$allowedLogRoot = [IO.Path]::GetFullPath((Join-Path $serverRepository 'Build\Logs')).TrimEnd('\') + '\'
if (-not $resolvedLog.StartsWith($allowedLogRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "LogPath must stay inside $allowedLogRoot"
}
New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($resolvedLog)) | Out-Null

Write-Host "Dedicated Server: $serverExecutable"
Write-Host "Network: $($profile.Name) ($($profile.NetworkCategory))"
Write-Host 'Expected game endpoint: UDP 7770'
Write-Host "Log: $resolvedLog"
Write-Host 'Press Ctrl+C to stop the server.'

& $serverExecutable -batchmode -nographics -logFile $resolvedLog
exit $LASTEXITCODE
