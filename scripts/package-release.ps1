[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [switch]$Locked
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'common.ps1')

Initialize-BuildEnvironment
$repositoryRoot = Get-RepositoryRoot
$dotnet = Get-DotNetExecutable
$applicationProject = Join-Path $repositoryRoot 'src\LibraTray.App\LibraTray.App.csproj'
$installerProject = Join-Path $repositoryRoot 'installer\LibraTray.Installer.wixproj'
$releaseParent = Join-Path $repositoryRoot 'artifacts\release'
$releaseRoot = Join-Path $releaseParent $Version
$portableName = "LibraTray-$Version-win-x64"
$portableDirectory = Join-Path $releaseRoot $portableName
$portableArchive = Join-Path $releaseRoot "$portableName.zip"
$installerOutput = Join-Path $releaseRoot 'installer'
$installerAsset = Join-Path $releaseRoot "$portableName.msi"
$checksumPath = Join-Path $releaseRoot 'SHA256SUMS.txt'
$restoreMode = if ($Locked) { '--locked-mode' } else { '--use-lock-file' }

function Invoke-DotNet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & $dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE."
    }
}

function Copy-RequiredFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Source,

        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
        throw "Required packaging input was not found: $Source"
    }

    Copy-Item -LiteralPath $Source -Destination $Destination
}

$resolvedReleaseParent = [IO.Path]::GetFullPath($releaseParent)
$resolvedReleaseRoot = [IO.Path]::GetFullPath($releaseRoot)
if (-not $resolvedReleaseRoot.StartsWith(
        "$resolvedReleaseParent$([IO.Path]::DirectorySeparatorChar)",
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Release output escaped the intended artifacts directory: $resolvedReleaseRoot"
}

if (Test-Path -LiteralPath $resolvedReleaseRoot) {
    Remove-Item -LiteralPath $resolvedReleaseRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $portableDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $installerOutput -Force | Out-Null

Push-Location $repositoryRoot
try {
    Invoke-DotNet -Arguments @(
        'restore',
        $applicationProject,
        '--runtime',
        'win-x64',
        $restoreMode
    )
    Invoke-DotNet -Arguments @(
        'publish',
        $applicationProject,
        '--configuration',
        'Release',
        '--runtime',
        'win-x64',
        '--self-contained',
        'true',
        '--no-restore',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        "-p:Version=$Version",
        "-p:AssemblyVersion=$Version.0",
        "-p:FileVersion=$Version.0",
        "-p:InformationalVersion=$Version",
        '-p:IncludeSourceRevisionInInformationalVersion=false',
        '--output',
        $portableDirectory
    )

    $dotnetRoot = Split-Path -Parent $dotnet
    Copy-RequiredFile `
        -Source (Join-Path $repositoryRoot 'LICENSE') `
        -Destination (Join-Path $portableDirectory 'LICENSE.txt')
    Copy-RequiredFile `
        -Source (Join-Path $repositoryRoot 'THIRD_PARTY_NOTICES.md') `
        -Destination (Join-Path $portableDirectory 'THIRD_PARTY_NOTICES.md')
    Copy-RequiredFile `
        -Source (Join-Path $repositoryRoot 'README.md') `
        -Destination (Join-Path $portableDirectory 'README.md')
    Copy-RequiredFile `
        -Source (Join-Path $repositoryRoot 'README.zh-CN.md') `
        -Destination (Join-Path $portableDirectory 'README.zh-CN.md')
    Copy-RequiredFile `
        -Source (Join-Path $dotnetRoot 'LICENSE.txt') `
        -Destination (Join-Path $portableDirectory 'DOTNET-LICENSE.txt')
    Copy-RequiredFile `
        -Source (Join-Path $dotnetRoot 'ThirdPartyNotices.txt') `
        -Destination (Join-Path $portableDirectory 'DOTNET-THIRD-PARTY-NOTICES.txt')

    $launcher = Join-Path $portableDirectory 'LibraTray.exe'
    if (-not (Test-Path -LiteralPath $launcher -PathType Leaf)) {
        throw 'The self-contained publish did not create LibraTray.exe.'
    }

    $unexpectedFiles = @(
        Get-ChildItem -LiteralPath $portableDirectory -File -Recurse |
        Where-Object {
            $_.Extension -in @('.pdb', '.json') `
                -or $_.Name -match '(?i)settings|diagnostic|\.log$'
        }
    )
    if ($unexpectedFiles.Count -gt 0) {
        throw "Unexpected development or user file in publish payload: $($unexpectedFiles.FullName -join ', ')"
    }

    Compress-Archive `
        -LiteralPath $portableDirectory `
        -DestinationPath $portableArchive `
        -CompressionLevel Optimal

    Invoke-DotNet -Arguments @(
        'restore',
        $installerProject,
        $restoreMode
    )
    Invoke-DotNet -Arguments @(
        'build',
        $installerProject,
        '--configuration',
        'Release',
        '--no-restore',
        '--no-incremental',
        "-p:ProductVersion=$Version",
        "-p:PublishDir=$portableDirectory",
        "-p:OutputPath=$installerOutput"
    )

    $builtInstaller = Join-Path $installerOutput "$portableName.msi"
    Copy-RequiredFile -Source $builtInstaller -Destination $installerAsset

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($portableArchive)
    try {
        $entries = @($archive.Entries)
        if ($entries.Count -eq 0) {
            throw 'The portable ZIP contains no entries.'
        }

        $invalidEntries = @(
            $entries | Where-Object {
                [IO.Path]::IsPathRooted($_.FullName) `
                    -or $_.FullName -match '(^|[\\/])\.\.([\\/]|$)'
            }
        )
        if ($invalidEntries.Count -gt 0) {
            throw "The portable ZIP contains an unsafe path: $($invalidEntries[0].FullName)"
        }

        $expectedLauncherEntry = "$portableName/LibraTray.exe"
        $entryNames = @($entries | ForEach-Object { $_.FullName.Replace('\', '/') })
        if ($expectedLauncherEntry -notin $entryNames) {
            throw "The portable ZIP is missing $expectedLauncherEntry."
        }
    }
    finally {
        $archive.Dispose()
    }

    $assets = @($portableArchive, $installerAsset)
    $checksumLines = foreach ($asset in $assets) {
        $hash = Get-FileHash -LiteralPath $asset -Algorithm SHA256
        "$($hash.Hash.ToLowerInvariant()) *$([IO.Path]::GetFileName($asset))"
    }
    [IO.File]::WriteAllLines(
        $checksumPath,
        $checksumLines,
        [Text.UTF8Encoding]::new($false))

    foreach ($line in Get-Content -LiteralPath $checksumPath) {
        if ($line -notmatch '^([0-9a-f]{64}) \*(.+)$') {
            throw "Invalid checksum line: $line"
        }

        $assetPath = Join-Path $releaseRoot $Matches[2]
        $actualHash = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash
        if ($actualHash -ne $Matches[1]) {
            throw "Checksum verification failed for $($Matches[2])."
        }
    }

    Write-Host "Release assets created in $releaseRoot"
    Get-ChildItem -LiteralPath $releaseRoot -File |
        Sort-Object -Property Name |
        Select-Object Name, Length
}
finally {
    Pop-Location
}
