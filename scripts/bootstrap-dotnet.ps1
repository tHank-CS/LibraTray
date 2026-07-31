[CmdletBinding()]
param(
    [string]$Version = '10.0.302'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$toolsDirectory = Join-Path $repositoryRoot '.tools'
$installDirectory = Join-Path $repositoryRoot '.dotnet'
$installerPath = Join-Path $toolsDirectory 'dotnet-install.ps1'

New-Item -ItemType Directory -Path $toolsDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null

if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    Write-Host 'Downloading the official dotnet-install script from dot.net...'
    Invoke-WebRequest `
        -Uri 'https://dot.net/v1/dotnet-install.ps1' `
        -OutFile $installerPath `
        -UseBasicParsing
}

$signature = Get-AuthenticodeSignature -LiteralPath $installerPath
$expectedPublisher = 'O=Microsoft Corporation'
if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid `
    -or $null -eq $signature.SignerCertificate `
    -or $signature.SignerCertificate.Subject.IndexOf(
        $expectedPublisher,
        [StringComparison]::OrdinalIgnoreCase) -lt 0) {
    throw "Refusing to execute dotnet-install.ps1: expected a valid Microsoft Authenticode signature, got '$($signature.Status)'."
}

Write-Host "Installing .NET SDK $Version into $installDirectory..."
$windowsPowerShell = (Get-Command powershell.exe -ErrorAction Stop).Source
& $windowsPowerShell `
    -NoProfile `
    -NonInteractive `
    -ExecutionPolicy Bypass `
    -File $installerPath `
    -Version $Version `
    -InstallDir $installDirectory `
    -NoPath

if ($LASTEXITCODE -ne 0) {
    throw "dotnet-install.ps1 failed with exit code $LASTEXITCODE."
}

& (Join-Path $installDirectory 'dotnet.exe') --info
