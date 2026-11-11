[CmdletBinding(DefaultParameterSetName = 'Publish')]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')][string]$Version,
    [Parameter(Mandatory)][ValidatePattern('^8\.0\.\d+$')][string]$DotNetRuntimeVersion,
    [Parameter(Mandatory, ParameterSetName = 'Installer')][string]$ApprovedPayload,
    [Parameter(Mandatory, ParameterSetName = 'Installer')][ValidatePattern('^[a-fA-F0-9]{64}$')][string]$ApprovedPayloadSha256,
    [Parameter(Mandatory, ParameterSetName = 'Installer')][string]$InnoCompiler
)

$ErrorActionPreference = 'Stop'
$installer = $PSCmdlet.ParameterSetName -eq 'Installer'
if ($installer) {
    $payload = (Resolve-Path -LiteralPath $ApprovedPayload).Path
    $compiler = (Resolve-Path -LiteralPath $InnoCompiler).Path
    if ((Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash -ne $ApprovedPayloadSha256) {
        throw 'The release payload does not match the approved SHA-256.'
    }
}

$repository = Split-Path -Parent $PSScriptRoot
$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a new output directory.' }
$publish = Join-Path $output 'app'
$project = Join-Path $repository 'src/Saylo.Desktop/Saylo.Desktop.csproj'
dotnet publish $project -c Release -r win-x64 --self-contained true -o $publish `
    "-p:Version=$Version" "-p:RuntimeFrameworkVersion=$DotNetRuntimeVersion" `
    -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "dotnet publish exited with $LASTEXITCODE." }
if (-not $installer) { return }

# The release team supplies a ZIP approved for redistribution. The development venv is never copied.
# Expected roots are resources/ and licenses/. App-local VC++ files belong in resources/native/.
$staging = Join-Path $output 'payload'
Expand-Archive -LiteralPath $payload -DestinationPath $staging
foreach ($required in @(
    'resources/whisper/models/ggml-base.bin',
    'resources/argos/models/argos-models.json',
    'resources/argos/models/es-en/model/model.bin',
    'resources/argos/models/es-en/bpe.model',
    'resources/argos/models/en-es/model/model.bin',
    'resources/argos/models/en-es/sentencepiece.model',
    'resources/argos/runtime/python.exe',
    'resources/pronunciation/cmudict/cmudict.dict',
    'resources/pronunciation/cmudict/LICENSE',
    'resources/native/vcruntime140.dll',
    'resources/native/vcruntime140_1.dll',
    'resources/native/msvcp140.dll'
)) {
    if (-not (Test-Path -LiteralPath (Join-Path $staging $required) -PathType Leaf)) {
        throw "The approved payload is incomplete: $required."
    }
}
$notices = Join-Path $staging 'licenses'
if (-not (Get-ChildItem -LiteralPath $notices -File -Recurse | Select-Object -First 1)) {
    throw 'Include the approved third-party notices with the payload.'
}
$resources = Join-Path $staging 'resources'
$runtime = Join-Path $resources 'argos/runtime'
foreach ($name in @('vcruntime140.dll', 'vcruntime140_1.dll', 'msvcp140.dll')) {
    $library = Join-Path $resources "native/$name"
    Copy-Item -LiteralPath $library -Destination $publish
    Copy-Item -LiteralPath $library -Destination $runtime
}
& (Join-Path $runtime 'python.exe') -I -B -c 'import pathlib,sys; assert pathlib.Path(sys.base_prefix).resolve() == pathlib.Path(sys.executable).resolve().parent; import ctranslate2, sentencepiece, subword_nmt, sacremoses'
if ($LASTEXITCODE -ne 0) { throw 'The private Argos Python runtime failed its isolation or import check.' }

New-Item -Path (Join-Path $publish 'resources') -ItemType Directory | Out-Null
foreach ($component in @('argos', 'pronunciation', 'whisper')) {
    Move-Item -LiteralPath (Join-Path $resources $component) -Destination (Join-Path $publish 'resources')
}
Move-Item -LiteralPath $notices -Destination (Join-Path $publish 'licenses')

& $compiler "/DAppVersion=$Version" "/DPublishDir=$publish" "/O$output" (Join-Path $PSScriptRoot 'SayloSetup.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup exited with $LASTEXITCODE." }
