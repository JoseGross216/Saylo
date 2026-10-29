param(
    [Parameter(Mandatory = $true)][string]$InputPath,
    [Parameter(Mandatory = $true)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$inputFile = (Resolve-Path -LiteralPath $InputPath).Path
$outputFile = [IO.Path]::GetFullPath($OutputPath)
if ($inputFile -eq $outputFile) { throw 'Input and output paths must differ.' }
$reader = [IO.BinaryReader]::new([IO.File]::OpenRead($inputFile))
try {
    if ([Text.Encoding]::ASCII.GetString($reader.ReadBytes(4)) -ne 'RIFF') { throw 'Expected RIFF WAV.' }
    $riffSize = $reader.ReadUInt32()
    if ($riffSize + 8L -ne $reader.BaseStream.Length) { throw 'Invalid RIFF length.' }
    if ([Text.Encoding]::ASCII.GetString($reader.ReadBytes(4)) -ne 'WAVE') { throw 'Expected WAVE container.' }
    $validFormat = $false
    $pcmBytes = $null
    while ($reader.BaseStream.Position + 8 -le $reader.BaseStream.Length) {
        $chunk = [Text.Encoding]::ASCII.GetString($reader.ReadBytes(4))
        $size = $reader.ReadUInt32()
        $nextPosition = $reader.BaseStream.Position + $size + ($size % 2)
        if ($nextPosition -gt $reader.BaseStream.Length) { throw 'Truncated WAV chunk.' }
        if ($chunk -eq 'fmt ') {
            if ($size -lt 16) { throw 'Invalid WAV format chunk.' }
            $encoding = $reader.ReadUInt16()
            $channels = $reader.ReadUInt16()
            $sampleRate = $reader.ReadUInt32()
            $byteRate = $reader.ReadUInt32()
            $blockAlign = $reader.ReadUInt16()
            $bits = $reader.ReadUInt16()
            if ($encoding -ne 1 -or $channels -ne 1 -or $sampleRate -ne 16000 -or
                $byteRate -ne 32000 -or $blockAlign -ne 2 -or $bits -ne 16) {
                throw 'Only uncompressed PCM16 mono 16000 Hz WAV is supported. No resampling is performed.'
            }
            $validFormat = $true
        }
        elseif ($chunk -eq 'data') {
            if ($null -ne $pcmBytes) { throw 'Multiple data chunks are not supported.' }
            if ($size -gt [int]::MaxValue) { throw 'Audio fixture is too large.' }
            $pcmBytes = $reader.ReadBytes([int]$size)
        }
        $reader.BaseStream.Position = $nextPosition
    }
    if (!$validFormat -or $null -eq $pcmBytes -or $pcmBytes.Length -eq 0 -or $pcmBytes.Length % 2 -ne 0) {
        throw 'Missing format or complete PCM16 frames.'
    }
    [IO.File]::WriteAllBytes($outputFile, $pcmBytes)
    Write-Output "PCM16 mono 16000 Hz written: $outputFile"
}
finally { $reader.Dispose() }
