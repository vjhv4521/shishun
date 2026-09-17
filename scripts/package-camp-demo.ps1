[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$questRepository = Split-Path -Parent $PSScriptRoot
$questOutput = Join-Path $questRepository 'Build\WindowsCampQuest'
if (-not (Test-Path -LiteralPath (Join-Path $questOutput 'HavenCamp.exe'))) {
    throw 'Build the Windows Camp Quest Demo in Unity first.'
}
dotnet publish (Join-Path $questRepository 'Server\Haven.Gateway\Haven.Gateway.csproj') `
    --configuration Release --self-contained false --output (Join-Path $questOutput 'Gateway')
if ($LASTEXITCODE -ne 0) { throw 'Gateway publish failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'start-camp-gateway.ps1') -Destination $questOutput
Copy-Item -LiteralPath (Join-Path $questRepository 'docs\CAMP_QUESTS.md') -Destination (Join-Path $questOutput 'README.md')
Copy-Item -LiteralPath (Join-Path $questRepository 'docs\CAMP_QUEST_VALIDATION.md') -Destination $questOutput
Copy-Item -LiteralPath (Join-Path $questRepository 'Assets\HavenCamp\Fonts\OFL.txt') -Destination (Join-Path $questOutput 'Noto-Font-License.txt')
Write-Host "Camp demo package: $questOutput. Copy the entire directory. Optional Gateway requires ASP.NET Core Runtime 8 or .NET 8 SDK."
