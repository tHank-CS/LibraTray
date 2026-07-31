[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$MockArguments
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'common.ps1')

Initialize-BuildEnvironment
$repositoryRoot = Get-RepositoryRoot
$dotnet = Get-DotNetExecutable
$project = Join-Path $repositoryRoot 'tools\LibraTray.MockDevice\LibraTray.MockDevice.csproj'

& $dotnet run --project $project --configuration Release -- @MockArguments
exit $LASTEXITCODE
