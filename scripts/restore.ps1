[CmdletBinding()]
param(
    [switch]$Locked
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'common.ps1')

Initialize-BuildEnvironment
$repositoryRoot = Get-RepositoryRoot
$dotnet = Get-DotNetExecutable
$solution = Join-Path $repositoryRoot 'LibraTray.slnx'
$restoreMode = if ($Locked) { '--locked-mode' } else { '--use-lock-file' }

Push-Location $repositoryRoot
try {
    & $dotnet restore $solution $restoreMode
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}
