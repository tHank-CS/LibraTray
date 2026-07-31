[CmdletBinding()]
param(
    [switch]$SkipRestore
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'common.ps1')

Initialize-BuildEnvironment
$repositoryRoot = Get-RepositoryRoot
$dotnet = Get-DotNetExecutable
$solution = Join-Path $repositoryRoot 'LibraTray.slnx'
$testResults = Join-Path $repositoryRoot 'TestResults'
$testRunTimestamp = [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
$testRunId = "$testRunTimestamp-$([Guid]::NewGuid().ToString('N'))"
$testRunResults = Join-Path $testResults $testRunId
$testProjects = Get-ChildItem `
    -LiteralPath (Join-Path $repositoryRoot 'tests') `
    -Filter '*.csproj' `
    -File `
    -Recurse |
    Sort-Object -Property FullName

if (@($testProjects).Count -eq 0) {
    throw 'No test projects were found under the tests directory.'
}

function Test-ContainsVulnerability {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Value
    )

    if ($Value -is [string] -or $Value.GetType().IsValueType) {
        return $false
    }

    if ($Value -is [System.Collections.IEnumerable]) {
        foreach ($item in $Value) {
            if ($null -ne $item -and (Test-ContainsVulnerability -Value $item)) {
                return $true
            }
        }

        return $false
    }

    foreach ($property in $Value.PSObject.Properties) {
        $isVulnerabilityList = $property.Name -eq 'vulnerabilities'
        $hasItems = $null -ne $property.Value -and @($property.Value).Count -gt 0
        if ($isVulnerabilityList -and $hasItems) {
            return $true
        }

        $hasNestedValue = $null -ne $property.Value
        if ($hasNestedValue -and (Test-ContainsVulnerability -Value $property.Value)) {
            return $true
        }
    }

    return $false
}

Push-Location $repositoryRoot
try {
    if (-not $SkipRestore) {
        & $dotnet restore $solution --locked-mode
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet restore failed with exit code $LASTEXITCODE."
        }
    }

    & $dotnet format $solution --verify-no-changes --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet format verification failed with exit code $LASTEXITCODE."
    }

    & $dotnet build $solution --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE."
    }

    foreach ($testProject in $testProjects) {
        $projectResults = Join-Path $testRunResults $testProject.BaseName
        New-Item -ItemType Directory -Path $projectResults -Force | Out-Null

        & $dotnet test $testProject.FullName `
            --configuration Release `
            --no-build `
            --collect 'Code Coverage;Format=Cobertura' `
            --logger 'trx;LogFileName=tests.trx' `
            --results-directory $projectResults
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet test failed for $($testProject.Name) with exit code $LASTEXITCODE."
        }

        $trxPath = Join-Path $projectResults 'tests.trx'
        if (-not (Test-Path -LiteralPath $trxPath -PathType Leaf)) {
            throw "dotnet test did not create the expected TRX report for $($testProject.Name)."
        }

        $coverageFiles = @(
            Get-ChildItem `
                -LiteralPath $projectResults `
                -Filter '*.cobertura.xml' `
                -File `
                -Recurse
        )
        if ($coverageFiles.Count -eq 0) {
            throw "dotnet test did not create Cobertura coverage for $($testProject.Name)."
        }

        [xml]$trx = Get-Content -LiteralPath $trxPath -Raw
        $counters = $trx.SelectSingleNode(
            "/*[local-name()='TestRun']/*[local-name()='ResultSummary']/*[local-name()='Counters']")
        $testCount = 0
        $executedCount = 0
        if ($null -eq $counters `
            -or -not [int]::TryParse($counters.GetAttribute('total'), [ref]$testCount) `
            -or -not [int]::TryParse(
                $counters.GetAttribute('executed'),
                [ref]$executedCount) `
            -or $testCount -le 0 `
            -or $executedCount -le 0) {
            throw "No tests were discovered and executed for $($testProject.Name)."
        }

        $testSummary = "$($testProject.Name): verified $executedCount executed " `
            + "of $testCount discovered tests."
        Write-Host $testSummary
    }

    $auditLines = & $dotnet package list `
        --project $solution `
        --vulnerable `
        --include-transitive `
        --no-restore `
        --format json `
        --output-version 1
    if ($LASTEXITCODE -ne 0) {
        throw "The vulnerable package audit failed with exit code $LASTEXITCODE."
    }

    $auditJson = $auditLines -join [Environment]::NewLine
    try {
        $audit = $auditJson | ConvertFrom-Json
    }
    catch {
        throw "The vulnerable package audit returned invalid JSON: $($_.Exception.Message)"
    }

    if (Test-ContainsVulnerability -Value $audit) {
        throw 'The vulnerable package audit found one or more vulnerable dependencies.'
    }

    Write-Host 'NuGet vulnerability audit passed: no vulnerable dependencies were reported.'
}
finally {
    Pop-Location
}
