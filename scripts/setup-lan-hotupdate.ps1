[CmdletBinding()]
param(
    [ValidateRange(1, 65535)]
    [int]$PatchPort = 5080,
    [ValidateRange(1, 65535)]
    [int]$GamePort = 7770,
    [ValidatePattern('^[0-9A-Za-z][0-9A-Za-z._-]*$')]
    [string]$AppVersion = '0.1.0',
    [switch]$SetPrivateProfile,
    [string]$ExpectedNetworkName,
    [switch]$EnableGameplay,
    [switch]$RemediateLegacyRules,
    [switch]$DiscoverOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

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
        InterfaceAlias = $configuration.InterfaceAlias
        InterfaceIndex = $configuration.InterfaceIndex
        IPv4Address = $ipv4Address
        NetworkName = $profile.Name
        NetworkCategory = [string]$profile.NetworkCategory
    }
}

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Set-LanFirewallRule {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [string]$Protocol,
        [Parameter(Mandatory)] [int]$Port
    )

    $rules = @(Get-NetFirewallRule -DisplayName $Name -ErrorAction SilentlyContinue)
    if ($rules.Count -eq 0) {
        New-NetFirewallRule -DisplayName $Name -Direction Inbound -Action Allow -Enabled True `
            -Protocol $Protocol -LocalPort $Port -Profile Private -RemoteAddress LocalSubnet | Out-Null
    }
    else {
        $rules | Set-NetFirewallRule -Direction Inbound -Action Allow -Enabled True -Profile Private
        $rules | Get-NetFirewallPortFilter | Set-NetFirewallPortFilter -Protocol $Protocol -LocalPort $Port
        $rules | Get-NetFirewallAddressFilter | Set-NetFirewallAddressFilter -RemoteAddress LocalSubnet
    }
}

$lan = Get-PrimaryLanContext
$patchHost = "http://$($lan.IPv4Address):$PatchPort/patches"
$legacyRules = @(Get-NetFirewallRule -DisplayName 'havencamp' -ErrorAction SilentlyContinue)
Write-Host "Active adapter: $($lan.InterfaceAlias)"
Write-Host "Network name: $($lan.NetworkName)"
Write-Host "Network category: $($lan.NetworkCategory)"
Write-Host "Server IPv4: $($lan.IPv4Address)"
Write-Host "Patch version URL: $patchHost/PC/$AppVersion/DefaultPackage.version"
Write-Host "Legacy broad 'havencamp' rules: $($legacyRules.Count)"

if ($DiscoverOnly) {
    Write-Host 'Discovery only: no network, firewall, or Unity project settings were changed.'
    return
}

if (-not (Test-IsAdministrator)) {
    throw 'Administrator privileges are required. Open PowerShell as Administrator.'
}

if ($lan.NetworkCategory -eq 'Public') {
    if (-not $SetPrivateProfile -or [string]::IsNullOrWhiteSpace($ExpectedNetworkName) -or $ExpectedNetworkName -cne $lan.NetworkName) {
        throw "Network '$($lan.NetworkName)' is Public. Only after confirming it is your own router/hotspot, use -SetPrivateProfile -ExpectedNetworkName '$($lan.NetworkName)'. Never do this on campus or public Wi-Fi."
    }
    Set-NetConnectionProfile -InterfaceIndex $lan.InterfaceIndex -NetworkCategory Private
    $lan.NetworkCategory = 'Private'
}

if ($lan.NetworkCategory -ne 'Private') {
    throw "Network category is $($lan.NetworkCategory); this setup only permits a Private network."
}

if ($legacyRules.Count -gt 0) {
    if (-not $RemediateLegacyRules) {
        throw "Found $($legacyRules.Count) legacy rules named 'havencamp'. After confirmation, use -RemediateLegacyRules to remove exactly those rules and create least-privilege replacements."
    }
    $legacyRules | Disable-NetFirewallRule
    $legacyRules | Remove-NetFirewallRule
    Write-Host "Removed $($legacyRules.Count) legacy 'havencamp' rules."
}

Set-LanFirewallRule -Name 'Haven Patch Server TCP 5080' -Protocol TCP -Port $PatchPort
if ($EnableGameplay) {
    Set-LanFirewallRule -Name 'Haven Game Server UDP 7770' -Protocol UDP -Port $GamePort
}

Write-Host 'LAN firewall setup completed. No tracked Unity settings were changed.'
Write-Host "Before building a distributable client: `$env:HAVEN_PATCH_BASE_URL = '$patchHost'"
Write-Host "Start Gateway: .\scripts\start-gateway.ps1 -Port $PatchPort -AppVersion $AppVersion"
Write-Host "Partner check: .\scripts\test-lan-hotupdate.ps1 -ServerAddress $($lan.IPv4Address) -Port $PatchPort -AppVersion $AppVersion"
