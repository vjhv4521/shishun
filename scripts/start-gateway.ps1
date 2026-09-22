[CmdletBinding()]
param(
    [string]$ApiKey = $env:DeepSeek__ApiKey,
    [string]$SharedToken = $env:Gateway__SharedToken,
    [ValidateSet('0.0.0.0', '127.0.0.1')]
    [string]$ListenAddress = '0.0.0.0',
    [ValidateRange(1, 65535)]
    [int]$Port = 5080,
    [ValidatePattern('^[0-9A-Za-z][0-9A-Za-z._-]*$')]
    [string]$AppVersion = '0.1.0',
    [switch]$SimulatePatchDownloadFailure
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$gatewayProject = Join-Path $repositoryRoot 'Server\Haven.Gateway\Haven.Gateway.csproj'
$patchVersionFile = Join-Path $repositoryRoot "Build\LocalServer\patches\PC\$AppVersion\DefaultPackage.version"

function Get-PrimaryLanContext {
    $configuration = Get-NetIPConfiguration -ErrorAction Stop |
        Where-Object {
            $_.IPv4DefaultGateway -and
            $_.IPv4Address -and
            $_.NetAdapter.Status -eq 'Up' -and
            $_.NetAdapter.HardwareInterface
        } |
        Sort-Object { $_.NetIPv4Interface.InterfaceMetric } |
        Select-Object -First 1

    if (-not $configuration) {
        throw 'No active physical adapter with an IPv4 default gateway was found. Connect to a private router or phone hotspot first.'
    }

    $ipv4Address = $configuration.IPv4Address |
        Where-Object { $_.AddressState -eq 'Preferred' -and $_.IPAddress -notlike '169.254.*' } |
        Select-Object -ExpandProperty IPAddress -First 1
    $profile = Get-NetConnectionProfile -InterfaceIndex $configuration.InterfaceIndex -ErrorAction Stop
    if ([string]::IsNullOrWhiteSpace($ipv4Address)) {
        throw "Adapter $($configuration.InterfaceAlias) has no usable IPv4 address."
    }

    [pscustomobject]@{
        IPv4Address = $ipv4Address
        NetworkName = $profile.Name
        NetworkCategory = [string]$profile.NetworkCategory
    }
}

$lanContext = $null
if ($ListenAddress -eq '0.0.0.0') {
    $lanContext = Get-PrimaryLanContext
    if ($lanContext.NetworkCategory -ne 'Private') {
        throw "Active network '$($lanContext.NetworkName)' is $($lanContext.NetworkCategory); refusing LAN binding. Switch to a private router/hotspot, then run .\scripts\setup-lan-hotupdate.ps1."
    }
}

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    Write-Warning 'DeepSeek API Key is not configured; patch hosting is unaffected.'
}

if (-not [string]::IsNullOrWhiteSpace($ApiKey)) {
    $env:DeepSeek__ApiKey = $ApiKey
}
if (-not [string]::IsNullOrWhiteSpace($SharedToken)) {
    $env:Gateway__SharedToken = $SharedToken
}

if (-not (Test-Path -LiteralPath $patchVersionFile -PathType Leaf)) {
    Write-Warning "Patch version file was not found: $patchVersionFile. Run Haven/Content/1. Build Current Assets and Publish Locally in Unity first."
}

$env:ASPNETCORE_URLS = "http://${ListenAddress}:$Port"
$env:PatchStorage__SimulateDownloadFailure = if ($SimulatePatchDownloadFailure) { 'true' } else { 'false' }
Write-Host "Gateway listen address: $($env:ASPNETCORE_URLS)"
Write-Host "Local health check: http://127.0.0.1:$Port/health"
if ($lanContext) {
    Write-Host "Partner health check: http://$($lanContext.IPv4Address):$Port/health"
    Write-Host "Patch version URL: http://$($lanContext.IPv4Address):$Port/patches/PC/$AppVersion/DefaultPackage.version"
}
if ($SimulatePatchDownloadFailure) {
    Write-Warning 'Patch download failure mode is active: version and manifest requests succeed, while .rawfile/.bundle requests return HTTP 503.'
}
Write-Host 'Press Ctrl+C to stop the server.'

dotnet run --project $gatewayProject --no-launch-profile -- --urls $env:ASPNETCORE_URLS
