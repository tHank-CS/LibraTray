[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ProbeArguments
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'common.ps1')

Initialize-BuildEnvironment
$repositoryRoot = Get-RepositoryRoot
$dotnet = Get-DotNetExecutable
$project = Join-Path $repositoryRoot 'tools\LibraTray.Probe\LibraTray.Probe.csproj'

& $dotnet run --project $project --configuration Release -- @ProbeArguments
exit $LASTEXITCODE
