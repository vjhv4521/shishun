param(
    [string]$ApiKey = $env:DeepSeek__ApiKey,
    [string]$SharedToken = $env:Gateway__SharedToken
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$gatewayProject = Join-Path $repositoryRoot 'Server\Haven.Gateway\Haven.Gateway.csproj'

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    Write-Warning 'DeepSeek API key is missing. Patch hosting will work; AIGC requests will use the game-server fallback.'
}

if (-not [string]::IsNullOrWhiteSpace($ApiKey)) {
    $env:DeepSeek__ApiKey = $ApiKey
}
if (-not [string]::IsNullOrWhiteSpace($SharedToken)) {
    $env:Gateway__SharedToken = $SharedToken
}
dotnet run --project $gatewayProject
