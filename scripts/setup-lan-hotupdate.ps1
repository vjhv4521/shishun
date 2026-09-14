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
        throw '未找到带 IPv4 默认网关的活动物理网卡。请先连接私人路由器或手机热点。'
    }

    $ipv4Address = $configuration.IPv4Address |
        Where-Object { $_.AddressState -eq 'Preferred' -and $_.IPAddress -notlike '169.254.*' } |
        Select-Object -ExpandProperty IPAddress -First 1
    $profile = Get-NetConnectionProfile -InterfaceIndex $configuration.InterfaceIndex -ErrorAction Stop
    if ([string]::IsNullOrWhiteSpace($ipv4Address)) {
        throw "网卡 $($configuration.InterfaceAlias) 没有可用的 IPv4 地址。"
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
Write-Host "活动网卡：$($lan.InterfaceAlias)"
Write-Host "网络名称：$($lan.NetworkName)"
Write-Host "网络类别：$($lan.NetworkCategory)"
Write-Host "服务器 IPv4：$($lan.IPv4Address)"
Write-Host "补丁版本地址：$patchHost/PC/$AppVersion/DefaultPackage.version"

if ($DiscoverOnly) {
    Write-Host '仅检测模式：没有修改网络、防火墙或 Unity 项目。'
    return
}

if (-not (Test-IsAdministrator)) {
    throw '此配置需要管理员权限。请以管理员身份打开 PowerShell。'
}

if ($lan.NetworkCategory -eq 'Public') {
    if (-not $SetPrivateProfile -or [string]::IsNullOrWhiteSpace($ExpectedNetworkName) -or $ExpectedNetworkName -cne $lan.NetworkName) {
        throw "当前网络 '$($lan.NetworkName)' 是 Public。仅在确认它是自己的路由器/热点后，使用 -SetPrivateProfile -ExpectedNetworkName '$($lan.NetworkName)'；不要在校园公共 Wi-Fi 上执行。"
    }
    Set-NetConnectionProfile -InterfaceIndex $lan.InterfaceIndex -NetworkCategory Private
    $lan.NetworkCategory = 'Private'
}

if ($lan.NetworkCategory -ne 'Private') {
    throw "当前网络类别是 $($lan.NetworkCategory)，本方案只允许 Private 网络。"
}

Set-LanFirewallRule -Name 'Haven Patch Server TCP 5080' -Protocol TCP -Port $PatchPort
if ($EnableGameplay) {
    Set-LanFirewallRule -Name 'Haven Game Server UDP 7770' -Protocol UDP -Port $GamePort
}

Write-Host '局域网防火墙配置完成；未更改任何已跟踪的 Unity 配置。'
Write-Host "制作可分发客户端前设置：`$env:HAVEN_PATCH_BASE_URL = '$patchHost'"
Write-Host "启动 Gateway：.\scripts\start-gateway.ps1 -Port $PatchPort -AppVersion $AppVersion"
Write-Host "合作伙伴测试：.\scripts\test-lan-hotupdate.ps1 -ServerAddress $($lan.IPv4Address) -Port $PatchPort -AppVersion $AppVersion"
