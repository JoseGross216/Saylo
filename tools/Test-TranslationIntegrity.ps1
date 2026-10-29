param(
    [Parameter(Mandatory = $true)][string]$PythonExecutable,
    [Parameter(Mandatory = $true)][string]$ModelDirectory,
    [string]$ReportDirectory,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $ReportDirectory) { $ReportDirectory = Join-Path $repoRoot 'artifacts/translation-integrity/replay' }
$settings = @{
    SAYLO_ARGOS_PYTHON = (Resolve-Path -LiteralPath $PythonExecutable).Path
    SAYLO_ARGOS_MODELS = (Resolve-Path -LiteralPath $ModelDirectory).Path
    SAYLO_INTEGRITY_REPORT_DIR = [IO.Path]::GetFullPath($ReportDirectory)
}
$previous = @{}
try {
    foreach ($name in $settings.Keys) {
        $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $settings[$name], 'Process')
    }
    $testArguments = @(
        'test', (Join-Path $repoRoot 'tests/Saylo.Modules.Translation.Tests/Saylo.Modules.Translation.Tests.csproj'),
        '--filter', 'Category=TranslationIntegrityIntegration', '--logger', 'console;verbosity=detailed'
    )
    if ($NoBuild) { $testArguments += @('--no-build', '--no-restore') }
    & dotnet @testArguments
    if ($LASTEXITCODE -ne 0) { throw "Translation integrity tests failed with exit code $LASTEXITCODE." }
}
finally {
    foreach ($name in $previous.Keys) {
        [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process')
    }
}
