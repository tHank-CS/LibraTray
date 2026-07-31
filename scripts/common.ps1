$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-RepositoryRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}

function Initialize-BuildEnvironment {
    $repositoryRoot = Get-RepositoryRoot

    $environmentDirectories = @{
        DOTNET_CLI_HOME = Join-Path $repositoryRoot '.dotnet-home'
        NUGET_PACKAGES  = Join-Path $repositoryRoot '.nuget\packages'
        TEMP            = Join-Path $repositoryRoot '.temp'
        TMP             = Join-Path $repositoryRoot '.temp'
    }

    foreach ($entry in $environmentDirectories.GetEnumerator()) {
        New-Item -ItemType Directory -Path $entry.Value -Force | Out-Null
        Set-Item -Path "Env:$($entry.Key)" -Value $entry.Value
    }

    $env:DOTNET_NOLOGO = '1'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
}

function Get-DotNetExecutable {
    $repositoryRoot = Get-RepositoryRoot
    $localDotNet = Join-Path $repositoryRoot '.dotnet\dotnet.exe'

    if (Test-Path -LiteralPath $localDotNet -PathType Leaf) {
        return $localDotNet
    }

    $installedDotNet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -ne $installedDotNet) {
        return $installedDotNet.Source
    }

    throw 'The .NET SDK was not found. Install the SDK version in global.json, or run scripts/bootstrap-dotnet.ps1.'
}
