[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Baseline', 'Patch')]
    [string]$ReleaseKind,
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+-[0-9A-Za-z.-]+$')]
    [string]$ContentVersion,
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9A-Za-z._-]+$')]
    [string]$GitTag,
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9.]+$')]
    [string]$ServerAddress,
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')]
    [string]$AppVersion = '0.1.0',
    [ValidatePattern('^[0-9A-Za-z._-]+$')]
    [string]$BaselineTag = 'demo-client-v0.1.0',
    [string]$UnityPath = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$releaseRepository = Split-Path -Parent $PSScriptRoot
$releaseBuildRoot = Join-Path $releaseRepository 'Build'
$releaseLogs = Join-Path $releaseBuildRoot 'Logs'
$patchDirectory = Join-Path $releaseBuildRoot "LocalServer\patches\PC\$AppVersion"
$versionPointer = Join-Path $patchDirectory 'DefaultPackage.version'

function Invoke-GitText {
    param([Parameter(Mandatory)] [string[]]$Arguments)
    $output = & git -C $releaseRepository @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed: $output" }
    ([string]($output -join "`n")).Trim()
}

function Test-PrivateIpv4 {
    param([Parameter(Mandatory)] [string]$Address)
    $parsed = [Net.IPAddress]::None
    if (-not [Net.IPAddress]::TryParse($Address, [ref]$parsed) -or $parsed.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork) { return $false }
    $bytes = $parsed.GetAddressBytes()
    if ([Net.IPAddress]::IsLoopback($parsed)) { return $false }
    $bytes[0] -eq 10 -or ($bytes[0] -eq 172 -and $bytes[1] -ge 16 -and $bytes[1] -le 31) -or ($bytes[0] -eq 192 -and $bytes[1] -eq 168)
}

function Assert-FormalGitState {
    $dirty = Invoke-GitText -Arguments @('status', '--porcelain')
    if (-not [string]::IsNullOrWhiteSpace($dirty)) {
        throw 'Formal releases require a clean Git worktree. Review and commit intended changes without discarding unrelated work.'
    }
    $tags = @(Invoke-GitText -Arguments @('tag', '--points-at', 'HEAD') -split "`n")
    if ($tags -notcontains $GitTag) { throw "HEAD must have the exact release tag '$GitTag'." }
}

function Assert-PatchCompatibility {
    Invoke-GitText -Arguments @('rev-parse', '--verify', "$BaselineTag^{commit}") | Out-Null
    $changed = @(Invoke-GitText -Arguments @('diff', '--name-only', "$BaselineTag..HEAD") -split "`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $blocked = @($changed | Where-Object {
        -not ($_.StartsWith('Assets/Hotfix/Runtime/', [StringComparison]::Ordinal) -or
              $_.StartsWith('Assets/Hotfix/Content/', [StringComparison]::Ordinal))
    })
    if ($blocked.Count -gt 0) {
        throw "Patch-only release changes AOT/client files and requires a new baseline client:`n$($blocked -join "`n")"
    }
}

function Invoke-UnityReleaseStep {
    param([Parameter(Mandatory)] [string]$Method, [Parameter(Mandatory)] [string]$LogName)
    $logPath = Join-Path $releaseLogs $LogName
    & $UnityPath -batchmode -nographics -quit -projectPath $releaseRepository -executeMethod $Method -logFile $logPath
    if ($LASTEXITCODE -ne 0) { throw "Unity step '$Method' failed. See $logPath" }
}

function New-ArtifactRecord {
    param([Parameter(Mandatory)] [string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Release artifact is missing: $Path" }
    $item = Get-Item -LiteralPath $Path
    $repositoryPrefix = [IO.Path]::GetFullPath($releaseRepository).TrimEnd('\') + '\'
    if (-not $item.FullName.StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Artifact is outside the repository: $($item.FullName)" }
    [ordered]@{
        path = $item.FullName.Substring($repositoryPrefix.Length).Replace('\', '/')
        bytes = $item.Length
        sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash
    }
}

if ([string]::IsNullOrWhiteSpace($UnityPath)) {
    $unityInstall = Get-ItemProperty 'HKLM:\SOFTWARE\Unity Technologies\Installer\Unity 6000.5.5f1' -ErrorAction SilentlyContinue
    if (-not $unityInstall) { $unityInstall = Get-ItemProperty 'HKCU:\SOFTWARE\Unity Technologies\Installer\Unity 6000.5.5f1' -ErrorAction SilentlyContinue }
    if ($unityInstall) { $UnityPath = Join-Path $unityInstall.'Location x64' 'Editor\Unity.exe' }
}
if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) { throw "Unity 6000.5.5f1 was not found: $UnityPath" }
if (-not (Test-PrivateIpv4 -Address $ServerAddress)) { throw 'ServerAddress must be a non-loopback RFC1918 IPv4 address.' }
if ($ContentVersion -notlike "$AppVersion-*") { throw "ContentVersion must start with '$AppVersion-'." }

$previousContentEnvironment = $env:HAVEN_CONTENT_VERSION
$previousPatchEnvironment = $env:HAVEN_PATCH_BASE_URL
$previousGameEnvironment = $env:HAVEN_GAME_SERVER_HOST
Push-Location $releaseRepository
try {
    Assert-FormalGitState
    $commit = Invoke-GitText -Arguments @('rev-parse', 'HEAD')
    $networkAsset = Get-Content -LiteralPath 'Assets/Resources/HavenNetworkSettings.asset' -Raw
    $protocolMatch = [regex]::Match($networkAsset, '(?m)^\s*protocolVersion:\s*(\d+)\s*$')
    if (-not $protocolMatch.Success) { throw 'Could not read the network protocol version.' }
    $protocolVersion = [int]$protocolMatch.Groups[1].Value
    $patchBaseUrl = "http://${ServerAddress}:5080/patches"
    New-Item -ItemType Directory -Force -Path $releaseLogs | Out-Null

    $previousContentVersion = if (Test-Path -LiteralPath $versionPointer) { (Get-Content -LiteralPath $versionPointer -Raw).Trim() } else { '' }
    $env:HAVEN_CONTENT_VERSION = $ContentVersion
    $env:HAVEN_PATCH_BASE_URL = $patchBaseUrl
    $env:HAVEN_GAME_SERVER_HOST = $ServerAddress

    if ($ReleaseKind -eq 'Baseline') {
        $localAddress = Get-NetIPAddress -AddressFamily IPv4 -IPAddress $ServerAddress -ErrorAction SilentlyContinue
        if (-not $localAddress) { throw "ServerAddress $ServerAddress is not assigned to this computer." }
        $profile = Get-NetConnectionProfile -InterfaceIndex $localAddress.InterfaceIndex -ErrorAction Stop
        if ($profile.NetworkCategory -ne 'Private') { throw "Network '$($profile.Name)' must be Private before a Hosted LAN build." }
        Invoke-UnityReleaseStep -Method 'Haven.Framework.Editor.HavenPlayerBuilder.BuildWindowsHostedClientBatch' -LogName "HostedClient-$ContentVersion.log"
        Invoke-UnityReleaseStep -Method 'Haven.Framework.Editor.HavenPlayerBuilder.BuildWindowsDedicatedServerBatch' -LogName "DedicatedServer-$ContentVersion.log"
    }
    else {
        Assert-PatchCompatibility
        Invoke-UnityReleaseStep -Method 'Haven.Framework.Editor.HavenContentBuilder.CompileHotfixAndPublish' -LogName "HotfixPatch-$ContentVersion.log"
    }

    # Generate All and asset preparation are allowed to run during a release,
    # but every tracked result must already be represented by the tagged commit.
    # Otherwise release.json would claim a clean source state that did not
    # actually produce the binaries.
    Assert-FormalGitState

    if (-not (Test-Path -LiteralPath $versionPointer)) { throw "Version pointer was not published: $versionPointer" }
    $publishedVersion = (Get-Content -LiteralPath $versionPointer -Raw).Trim()
    if ($publishedVersion -ne $ContentVersion) { throw "Published version '$publishedVersion' does not match '$ContentVersion'." }

    $releaseName = if ($ReleaseKind -eq 'Baseline') { "client-$AppVersion-$ContentVersion" } else { "patch-$AppVersion-$ContentVersion" }
    $releaseDirectory = Join-Path $releaseBuildRoot "Releases\$releaseName"
    if (Test-Path -LiteralPath $releaseDirectory) { throw "Immutable release directory already exists: $releaseDirectory" }
    New-Item -ItemType Directory -Path $releaseDirectory | Out-Null

    $artifacts = @()
    if ($ReleaseKind -eq 'Baseline') {
        $clientArchive = Join-Path $releaseDirectory 'HavenClient.zip'
        $serverArchive = Join-Path $releaseDirectory 'HavenServer.zip'
        Compress-Archive -Path (Join-Path $releaseBuildRoot 'WindowsClient\*') -DestinationPath $clientArchive -CompressionLevel Optimal
        Compress-Archive -Path (Join-Path $releaseBuildRoot 'WindowsServer\*') -DestinationPath $serverArchive -CompressionLevel Optimal
        $artifacts += New-ArtifactRecord -Path $clientArchive
        $artifacts += New-ArtifactRecord -Path $serverArchive
        $artifacts += New-ArtifactRecord -Path (Join-Path $releaseBuildRoot 'WindowsClient\HavenClient.exe')
        $artifacts += New-ArtifactRecord -Path (Join-Path $releaseBuildRoot 'WindowsClient\GameAssembly.dll')
        $artifacts += New-ArtifactRecord -Path (Join-Path $releaseBuildRoot 'WindowsServer\HavenServer.exe')
        $artifacts += New-ArtifactRecord -Path (Join-Path $releaseBuildRoot 'WindowsServer\HavenServer_Data\Managed\Haven.Network.FishNet.dll')
    }
    $manifestPath = Join-Path $patchDirectory "DefaultPackage_${ContentVersion}.bytes"
    $artifacts += New-ArtifactRecord -Path $manifestPath
    $artifacts += New-ArtifactRecord -Path $versionPointer

    $release = [ordered]@{
        schemaVersion = 1
        releaseKind = $ReleaseKind.ToLowerInvariant()
        appVersion = $AppVersion
        contentVersion = $ContentVersion
        previousContentVersion = $previousContentVersion
        compatibleBaselineTag = if ($ReleaseKind -eq 'Baseline') { $GitTag } else { $BaselineTag }
        gitCommit = $commit
        gitTag = $GitTag
        workingTreeClean = $true
        unityVersion = '6000.5.5f1'
        networkProtocolVersion = $protocolVersion
        patchBaseUrl = $patchBaseUrl
        gameServer = "${ServerAddress}:7770"
        builtAtUtc = [DateTime]::UtcNow.ToString('o')
        artifacts = $artifacts
    }
    $releaseJson = $release | ConvertTo-Json -Depth 8
    $releaseJsonPath = Join-Path $releaseDirectory 'release.json'
    Set-Content -LiteralPath $releaseJsonPath -Value $releaseJson -Encoding utf8
    Copy-Item -LiteralPath $releaseJsonPath -Destination (Join-Path $patchDirectory "release-$ContentVersion.json")
    Write-Host "Immutable release record: $releaseJsonPath"
}
finally {
    if ($null -eq $previousContentEnvironment) { Remove-Item Env:HAVEN_CONTENT_VERSION -ErrorAction SilentlyContinue } else { $env:HAVEN_CONTENT_VERSION = $previousContentEnvironment }
    if ($null -eq $previousPatchEnvironment) { Remove-Item Env:HAVEN_PATCH_BASE_URL -ErrorAction SilentlyContinue } else { $env:HAVEN_PATCH_BASE_URL = $previousPatchEnvironment }
    if ($null -eq $previousGameEnvironment) { Remove-Item Env:HAVEN_GAME_SERVER_HOST -ErrorAction SilentlyContinue } else { $env:HAVEN_GAME_SERVER_HOST = $previousGameEnvironment }
    Pop-Location
}
