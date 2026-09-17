[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
# Camp demo is single-player. Never inherit the LAN listener from another workflow.
$env:ASPNETCORE_URLS = 'http://127.0.0.1:5080'
if ([string]::IsNullOrWhiteSpace($env:DeepSeek__ApiKey)) {
    Write-Warning 'DeepSeek Key is not configured. The game will use local camp quests.'
}
Write-Host 'Camp Gateway: http://127.0.0.1:5080/health. Press Ctrl+C to stop.'
$packagedGateway = Join-Path $PSScriptRoot 'Gateway\Haven.Gateway.dll'
if (Test-Path -LiteralPath $packagedGateway) {
    Push-Location (Split-Path -Parent $packagedGateway)
    try { dotnet $packagedGateway --PatchStorage:Root (Join-Path $PSScriptRoot 'patches') }
    finally { Pop-Location }
} else {
    $questRepository = Split-Path -Parent $PSScriptRoot
    dotnet run --project (Join-Path $questRepository 'Server\Haven.Gateway\Haven.Gateway.csproj') --no-launch-profile
}
if ($LASTEXITCODE -ne 0) { throw "Gateway exited with code $LASTEXITCODE" }
