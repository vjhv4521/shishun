[CmdletBinding()]
param(
    [string]$ApiKey = $env:DeepSeek__ApiKey,
    [string]$SharedToken = $env:Gateway__SharedToken,
    [ValidateSet('0.0.0.0', '127.0.0.1')]
    [string]$ListenAddress = '0.0.0.0',
    [ValidateRange(1, 65535)]
    [int]$Port = 5080,
    [ValidatePattern('^[0-9A-Za-z][0-9A-Za-z._-]*$')]
    [string]$AppVersion = '0.1.0'
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
        IPv4Address = $ipv4Address
        NetworkName = $profile.Name
        NetworkCategory = [string]$profile.NetworkCategory
    }
}

$lanContext = $null
if ($ListenAddress -eq '0.0.0.0') {
    $lanContext = Get-PrimaryLanContext
    if ($lanContext.NetworkCategory -ne 'Private') {
        throw "当前活动网络 '$($lanContext.NetworkName)' 是 $($lanContext.NetworkCategory)，拒绝对局域网监听。请切换到私人路由器/热点，并以管理员身份运行 .\scripts\setup-lan-hotupdate.ps1 -SetPrivateProfile。"
    }
}

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    Write-Warning '未配置 DeepSeek API Key；补丁托管不受影响，本阶段可以忽略此警告。'
}

if (-not [string]::IsNullOrWhiteSpace($ApiKey)) {
    $env:DeepSeek__ApiKey = $ApiKey
}
if (-not [string]::IsNullOrWhiteSpace($SharedToken)) {
    $env:Gateway__SharedToken = $SharedToken
}

if (-not (Test-Path -LiteralPath $patchVersionFile -PathType Leaf)) {
    Write-Warning "尚未找到补丁版本文件：$patchVersionFile。请先在 Unity 执行 Haven/Content/1. Build Current Assets and Publish Locally。"
}

$env:ASPNETCORE_URLS = "http://${ListenAddress}:$Port"

Write-Host "Gateway 监听地址：$($env:ASPNETCORE_URLS)"
Write-Host "本机健康检查：http://127.0.0.1:$Port/health"
if ($lanContext) {
    Write-Host "合作伙伴健康检查：http://$($lanContext.IPv4Address):$Port/health"
    Write-Host "补丁版本地址：http://$($lanContext.IPv4Address):$Port/patches/PC/$AppVersion/DefaultPackage.version"
}
Write-Host '按 Ctrl+C 停止服务器。'

dotnet run --project $gatewayProject --no-launch-profile
