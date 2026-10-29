param(
    [string]$DestinationDirectory,
    [string]$SourceDirectory
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $DestinationDirectory) { $DestinationDirectory = Join-Path $repoRoot 'artifacts/pronunciation/cmudict' }
$version = '74790861f652b15e4ac49015a90074ad62a27690'
$files = @(
    @{ Name = 'cmudict.dict'; Size = 3618488; Hash = '81917843c7f44ce2b094ac63873c2c7a4cf802040792c455ba3ca406891c3d22' },
    @{ Name = 'LICENSE'; Size = 1754; Hash = 'bd4ce8e44170a5f9f481310ca85c51de3c4f851a65e679b40e603b143bd3542a' }
)
$destination = [IO.Path]::GetFullPath($DestinationDirectory)
$client = [Net.Http.HttpClient]::new()
try {
    $verified = @{}
    foreach ($file in $files) {
        $target = Join-Path $destination $file.Name
        if (Test-Path -LiteralPath $target) { $bytes = [IO.File]::ReadAllBytes($target) }
        elseif ($SourceDirectory) { $bytes = [IO.File]::ReadAllBytes((Join-Path (Resolve-Path -LiteralPath $SourceDirectory).Path $file.Name)) }
        else { $bytes = $client.GetByteArrayAsync("https://raw.githubusercontent.com/cmusphinx/cmudict/$version/$($file.Name)").GetAwaiter().GetResult() }
        $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
        if ($bytes.Length -ne $file.Size -or $hash -ne $file.Hash) { throw "Resource verification failed: $($file.Name). Existing files were not overwritten." }
        $verified[$file.Name] = $bytes
    }
    [IO.Directory]::CreateDirectory($destination) | Out-Null
    foreach ($file in $files) {
        $target = Join-Path $destination $file.Name
        if (-not (Test-Path -LiteralPath $target)) {
            $stream = [IO.File]::Open($target, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
            try { $stream.Write($verified[$file.Name], 0, $verified[$file.Name].Length) }
            finally { $stream.Dispose() }
        }
    }
    Write-Output "Verified CMUdict $version and its redistribution notice at $destination"
}
finally { $client.Dispose() }
