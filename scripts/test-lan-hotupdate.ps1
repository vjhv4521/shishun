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

Write-Host "检查 TCP ${ServerAddress}:$Port ..."
$connection = Test-NetConnection -ComputerName $ServerAddress -Port $Port -WarningAction SilentlyContinue
if (-not $connection.TcpTestSucceeded) {
    throw "无法连接 TCP ${ServerAddress}:$Port。请检查两台电脑是否在同一私人局域网、Gateway 是否运行，以及防火墙规则是否存在。"
}

Write-Host "检查 $healthUrl ..."
$health = Invoke-RestMethod -Uri $healthUrl -Method Get -TimeoutSec 10
if ($health.status -ne 'ok' -or $health.patchHosting -ne $true) {
    throw "Gateway 健康检查未通过：status=$($health.status), patchHosting=$($health.patchHosting)"
}

Write-Host "检查 $versionUrl ..."
$versionResponse = Invoke-WebRequest -Uri $versionUrl -Method Get -TimeoutSec 10
if ($versionResponse.Content -is [byte[]]) {
    $publishedVersion = [Text.Encoding]::UTF8.GetString($versionResponse.Content).Trim()
}
else {
    $publishedVersion = ([string]$versionResponse.Content).Trim()
}
if ([string]::IsNullOrWhiteSpace($publishedVersion)) {
    throw '补丁版本文件存在，但内容为空。'
}

[pscustomobject]@{
    Tcp5080 = 'ok'
    Gateway = $health.status
    PatchHosting = $health.patchHosting
    PublishedVersion = $publishedVersion
    VersionUrl = $versionUrl
}
