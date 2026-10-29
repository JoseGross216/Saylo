[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$NativeAudio,
    [switch]$IncludeTranslation,
    [switch]$NoRestore,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $repoRoot 'artifacts/package6'
$resultsRoot = Join-Path $artifactRoot 'test-results'
$previousNativeAudio = [Environment]::GetEnvironmentVariable('SAYLO_NATIVE_AUDIO_TESTS', 'Process')
$previousWorkloadResolver = [Environment]::GetEnvironmentVariable('MSBuildEnableWorkloadResolver', 'Process')
$suites = @('Saylo.Desktop.Tests', 'Saylo.Modules.Conversations.Tests', 'Saylo.Architecture.Tests')
if ($IncludeTranslation) { $suites += 'Saylo.Modules.Translation.Tests' }
$summary = [Collections.Generic.List[object]]::new()
$failed = $false

Push-Location $repoRoot
try {
    $env:MSBuildEnableWorkloadResolver = 'false'
    $env:SAYLO_NATIVE_AUDIO_TESTS = if ($NativeAudio) { '1' } else { '0' }
    New-Item -ItemType Directory -Path $resultsRoot -Force | Out-Null
    foreach ($suite in $suites) {
        $project = Join-Path $repoRoot "tests/$suite/$suite.csproj"
        $trxName = "$suite.trx"
        $trxPath = Join-Path $resultsRoot $trxName
        # A failed build must never be reported with a previous run's test results.
        if (Test-Path -LiteralPath $trxPath) { Remove-Item -LiteralPath $trxPath }
        $arguments = @('test', $project, '-c', $Configuration, '--artifacts-path', $artifactRoot,
            '--logger', "trx;LogFileName=$trxName", '--results-directory', $resultsRoot, '-v', 'minimal')
        if ($NoRestore) { $arguments += '--no-restore' }
        if ($NoBuild) { $arguments += '--no-build' }
        & dotnet @arguments
        $suiteExit = $LASTEXITCODE
        $counts = $null
        if (Test-Path -LiteralPath $trxPath) {
            [xml]$trx = Get-Content -LiteralPath $trxPath -Raw
            $counts = $trx.SelectSingleNode("/*[local-name()='TestRun']/*[local-name()='ResultSummary']/*[local-name()='Counters']")
        }
        $passed = if ($counts) { [int]$counts.passed } else { 0 }
        $failures = if ($counts) { [int]$counts.failed } else { 0 }
        # xUnit's TRX adapter can leave Counters.notExecuted at zero for skipped tests.
        $skipped = if ($counts) {
            $trx.SelectNodes("/*[local-name()='TestRun']/*[local-name()='Results']/*[local-name()='UnitTestResult'][@outcome='NotExecuted']").Count
        } else { 0 }
        $total = if ($counts) { [int]$counts.total } else { 0 }
        $suiteFailed = $suiteExit -ne 0 -or !$counts -or $total -eq 0 -or $failures -gt 0
        if ($suiteFailed) { $failed = $true }
        $summary.Add([pscustomobject]@{
            suite = $suite; exitCode = $suiteExit; passed = $passed; failed = $failures
            skipped = $skipped; total = $total; successful = !$suiteFailed; resultFile = $trxName
        })
    }
    [pscustomobject]@{
        completedAtUtc = [DateTime]::UtcNow.ToString('o')
        configuration = $Configuration
        nativeAudioRequested = [bool]$NativeAudio
        translationIncluded = [bool]$IncludeTranslation
        buildSkipped = [bool]$NoBuild
        successful = !$failed
        suites = @($summary.ToArray())
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $resultsRoot 'summary.json') -Encoding utf8
    $summary | Format-Table suite, passed, failed, skipped, successful -AutoSize
    Write-Output "Resultados: $resultsRoot"
}
finally {
    [Environment]::SetEnvironmentVariable('SAYLO_NATIVE_AUDIO_TESTS', $previousNativeAudio, 'Process')
    [Environment]::SetEnvironmentVariable('MSBuildEnableWorkloadResolver', $previousWorkloadResolver, 'Process')
    Pop-Location
}
if ($failed) { exit 1 }
