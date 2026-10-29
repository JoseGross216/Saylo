param(
    [Parameter(Mandatory = $true)][string]$PythonExecutable,
    [Parameter(Mandatory = $true)][string]$ModelDirectory,
    [string]$ReportPath
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$settings = @{
    SAYLO_ARGOS_PYTHON = (Resolve-Path -LiteralPath $PythonExecutable).Path
    SAYLO_ARGOS_MODELS = (Resolve-Path -LiteralPath $ModelDirectory).Path
    SAYLO_ARGOS_REPORT = $(if ($ReportPath) { [IO.Path]::GetFullPath($ReportPath) } else { '' })
}
$previous = @{}
try {
    foreach ($name in $settings.Keys) {
        $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $settings[$name], 'Process')
    }
    & dotnet test (Join-Path $repoRoot 'tests/Saylo.Modules.Translation.Tests/Saylo.Modules.Translation.Tests.csproj') `
        --filter 'Category=ArgosIntegration' --logger 'console;verbosity=detailed'
    if ($LASTEXITCODE -ne 0) { throw "Argos integration tests failed with exit code $LASTEXITCODE." }
}
finally {
    foreach ($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}
