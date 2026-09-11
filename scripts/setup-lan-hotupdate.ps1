[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [ValidateRange(1, 65535)]
    [int]$Port = 5080,
    [ValidatePattern('^[0-9A-Za-z][0-9A-Za-z._-]*$')]
    [string]$AppVersion = '0.1.0',
    [switch]$SetPrivateProfile,
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

function Set-UnityHotUpdateSettings {
    param(
        [Parameter(Mandatory)]
        [string]$SettingsPath,
        [Parameter(Mandatory)]
        [string]$PatchHost,
        [Parameter(Mandatory)]
        [string]$Version
    )

    $content = [IO.File]::ReadAllText($SettingsPath)
    $replacements = @(
        @{ Pattern = '(?m)^  playMode: .*$'; Value = '  playMode: 0'; Name = 'playMode' },
        @{ Pattern = '(?m)^  primaryHost: .*$'; Value = "  primaryHost: $PatchHost"; Name = 'primaryHost' },
        @{ Pattern = '(?m)^  fallbackHost: .*$'; Value = "  fallbackHost: $PatchHost"; Name = 'fallbackHost' },
        @{ Pattern = '(?m)^  appVersion: .*$'; Value = "  appVersion: $Version"; Name = 'appVersion' },
        @{ Pattern = '(?m)^  appendPlatformAndVersion: .*$'; Value = '  appendPlatformAndVersion: 1'; Name = 'appendPlatformAndVersion' }
    )

    foreach ($replacement in $replacements) {
        $matchCount = [regex]::Matches($content, $replacement.Pattern).Count
        if ($matchCount -ne 1) {
            throw "无法安全更新 $($replacement.Name)：预期匹配 1 行，实际匹配 $matchCount 行。"
        }
        $content = [regex]::Replace($content, $replacement.Pattern, $replacement.Value)
    }

    $utf8WithoutBom = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText($SettingsPath, $content, $utf8WithoutBom)
}

$lanContext = Get-PrimaryLanContext
$patchHost = "http://$($lanContext.IPv4Address):$Port/patches"
$versionUrl = "$patchHost/PC/$AppVersion/DefaultPackage.version"

Write-Host "活动网卡：$($lanContext.InterfaceAlias)"
Write-Host "网络名称：$($lanContext.NetworkName)"
Write-Host "网络类别：$($lanContext.NetworkCategory)"
Write-Host "服务器 IPv4：$($lanContext.IPv4Address)"
Write-Host "补丁版本地址：$versionUrl"

if ($DiscoverOnly) {
    Write-Host '仅检测模式：未修改网络、防火墙或 Unity 配置。'
    return
}

if (-not (Test-IsAdministrator)) {
    throw '此配置需要管理员权限。请以管理员身份打开 PowerShell，再重新运行脚本。'
}

if ($lanContext.NetworkCategory -eq 'Public') {
    if (-not $SetPrivateProfile) {
        throw "当前网络 '$($lanContext.NetworkName)' 是 Public。确认它是你自己的路由器或手机热点后，添加 -SetPrivateProfile 重新运行；不要在校园公共 Wi-Fi 上执行。"
    }

    if ($PSCmdlet.ShouldProcess($lanContext.NetworkName, '将当前网络配置文件改为 Private')) {
        Set-NetConnectionProfile -InterfaceIndex $lanContext.InterfaceIndex -NetworkCategory Private
        $lanContext.NetworkCategory = 'Private'
    }
}

if ($lanContext.NetworkCategory -ne 'Private') {
    throw "当前网络类别是 $($lanContext.NetworkCategory)，本方案只允许 Private 网络。"
}

$ruleName = 'Haven Patch Server TCP 5080'
$existingRules = @(Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)
if ($PSCmdlet.ShouldProcess($ruleName, "允许 Private/LocalSubnet 入站 TCP $Port")) {
    if ($existingRules.Count -eq 0) {
        New-NetFirewallRule `
            -DisplayName $ruleName `
            -Direction Inbound `
            -Action Allow `
            -Enabled True `
            -Protocol TCP `
            -LocalPort $Port `
            -Profile Private `
            -RemoteAddress LocalSubnet | Out-Null
    }
    else {
        $existingRules | Set-NetFirewallRule -Direction Inbound -Action Allow -Enabled True -Profile Private
        $existingRules | Get-NetFirewallPortFilter | Set-NetFirewallPortFilter -Protocol TCP -LocalPort $Port
        $existingRules | Get-NetFirewallAddressFilter | Set-NetFirewallAddressFilter -RemoteAddress LocalSubnet
    }
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$settingsPath = Join-Path $repositoryRoot 'Assets\Resources\HavenHotUpdateSettings.asset'
if (-not (Test-Path -LiteralPath $settingsPath -PathType Leaf)) {
    throw "找不到 Unity 热更新配置：$settingsPath"
}

if ($PSCmdlet.ShouldProcess($settingsPath, "设置补丁地址为 $patchHost")) {
    Set-UnityHotUpdateSettings -SettingsPath $settingsPath -PatchHost $patchHost -Version $AppVersion
}

Write-Host ''
Write-Host '局域网热更新配置完成。'
Write-Host "启动服务器：.\scripts\start-gateway.ps1 -Port $Port -AppVersion $AppVersion"
Write-Host "合作伙伴测试：.\scripts\test-lan-hotupdate.ps1 -ServerAddress $($lanContext.IPv4Address) -Port $Port -AppVersion $AppVersion"
