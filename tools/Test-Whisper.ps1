param(
    [Parameter(Mandatory = $true)][string]$ModelDirectory,
    [Parameter(Mandatory = $true)][string]$SpanishPcm,
    [Parameter(Mandatory = $true)][string]$EnglishPcm,
    [Parameter(Mandatory = $true)][string]$SpanishExpected,
    [Parameter(Mandatory = $true)][string]$EnglishExpected
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$settings = @{
    SAYLO_WHISPER_MODEL_DIR = (Resolve-Path -LiteralPath $ModelDirectory).Path
    SAYLO_WHISPER_ES_PCM = (Resolve-Path -LiteralPath $SpanishPcm).Path
    SAYLO_WHISPER_EN_PCM = (Resolve-Path -LiteralPath $EnglishPcm).Path
    SAYLO_WHISPER_ES_EXPECTED = $SpanishExpected
    SAYLO_WHISPER_EN_EXPECTED = $EnglishExpected
}
$previous = @{}
try {
    foreach ($name in $settings.Keys) {
        $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $settings[$name], 'Process')
    }
    & dotnet test (Join-Path $repoRoot 'tests\Saylo.Modules.Translation.Tests\Saylo.Modules.Translation.Tests.csproj') `
        --filter 'Category=WhisperIntegration' --logger 'console;verbosity=detailed'
    if ($LASTEXITCODE -ne 0) { throw "Whisper integration tests failed with exit code $LASTEXITCODE." }
}
finally {
    foreach ($name in $previous.Keys) {
        [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process')
    }
}
