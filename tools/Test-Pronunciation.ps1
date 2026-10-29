param(
    [Parameter(Mandatory = $true)][string]$DictionaryDirectory,
    [string]$ReportDirectory,
    [switch]$IncludePipeline,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $ReportDirectory) { $ReportDirectory = Join-Path $repoRoot 'artifacts/pronunciation/replay' }
if ($IncludePipeline) {
    foreach ($name in @('SAYLO_ARGOS_PYTHON', 'SAYLO_ARGOS_MODELS', 'SAYLO_WHISPER_MODEL_DIR', 'SAYLO_WHISPER_EN_PCM')) {
        if (-not [Environment]::GetEnvironmentVariable($name, 'Process')) { throw "Configure $name before requesting the real pipeline." }
    }
}
$settings = @{
    SAYLO_CMUDICT_DIR = (Resolve-Path -LiteralPath $DictionaryDirectory).Path
    SAYLO_PRONUNCIATION_REPORT_DIR = [IO.Path]::GetFullPath($ReportDirectory)
}
$previous = @{}
try {
    foreach ($name in $settings.Keys) {
        $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $settings[$name], 'Process')
    }
    $filter = 'Category=PronunciationIntegration'
    if ($IncludePipeline) { $filter += '|Category=PronunciationPipelineIntegration' }
    $testArguments = @('test', (Join-Path $repoRoot 'tests/Saylo.Modules.Translation.Tests/Saylo.Modules.Translation.Tests.csproj'),
        '--filter', $filter, '--logger', 'console;verbosity=normal')
    if ($NoBuild) { $testArguments += @('--no-build', '--no-restore') }
    & dotnet @testArguments
    if ($LASTEXITCODE -ne 0) { throw "Pronunciation tests failed with exit code $LASTEXITCODE." }
}
finally {
    foreach ($name in $previous.Keys) {
        [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process')
    }
}
