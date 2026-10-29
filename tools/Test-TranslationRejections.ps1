param(
    [Parameter(Mandatory = $true)][string]$PythonExecutable,
    [Parameter(Mandatory = $true)][string]$ModelDirectory,
    [string]$ReportDirectory,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $ReportDirectory) { $ReportDirectory = Join-Path $repoRoot 'artifacts/translation-rejections/replay' }
$settings = @{
    SAYLO_ARGOS_PYTHON = (Resolve-Path -LiteralPath $PythonExecutable).Path
    SAYLO_ARGOS_MODELS = (Resolve-Path -LiteralPath $ModelDirectory).Path
    SAYLO_REJECTION_REPORT_DIR = [IO.Path]::GetFullPath($ReportDirectory)
}
$previous = @{}
try {
    foreach ($name in $settings.Keys) {
        $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $settings[$name], 'Process')
    }
    $testArguments = @(
        'test', (Join-Path $repoRoot 'tests/Saylo.Modules.Translation.Tests/Saylo.Modules.Translation.Tests.csproj'),
        '--filter', 'Category=TranslationRejectionIntegration', '--logger', 'console;verbosity=detailed'
    )
    if ($NoBuild) { $testArguments += @('--no-build', '--no-restore') }
    & dotnet @testArguments
    if ($LASTEXITCODE -ne 0) { throw "Translation rejection tests failed with exit code $LASTEXITCODE." }
    foreach ($profile in @('previous-profile-v1', 'customer-service-v2')) {
        & $settings.SAYLO_ARGOS_PYTHON -I -B (Join-Path $PSScriptRoot 'Compare-TranslationRejections.py') `
            --new (Join-Path $settings.SAYLO_REJECTION_REPORT_DIR "$profile-corpus.json") `
            --output (Join-Path $settings.SAYLO_REJECTION_REPORT_DIR "$profile-comparison.json")
        if ($LASTEXITCODE -ne 0) { throw "Evidence comparison failed with exit code $LASTEXITCODE." }
    }
}
finally {
    foreach ($name in $previous.Keys) {
        [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process')
    }
}
