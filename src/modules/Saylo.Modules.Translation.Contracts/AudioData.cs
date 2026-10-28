namespace Saylo.Modules.Translation.Contracts;

/// <summary>Interleaved, signed 16-bit little-endian PCM held only in memory.</summary>
public enum AudioFormat
{
    Pcm16 = 1
}

/// <summary>
/// Owns a snapshot of audio bytes and the metadata needed for local playback.
/// Capture and engine adapters must decode containers into the supported format.
/// No file paths or persistent storage are involved.
/// </summary>
public sealed class AudioData
{
    public ReadOnlyMemory<byte> Data { get; }
    public AudioFormat Format { get; }
    public int SampleRate { get; }
    public int Channels { get; }
    public int BitsPerSample => 16;

    public AudioData(ReadOnlyMemory<byte> data, AudioFormat format, int sampleRate, int channels)
    {
        if (data.IsEmpty)
            throw new ArgumentException("Audio data must not be empty.", nameof(data));
        if (format != AudioFormat.Pcm16)
            throw new ArgumentOutOfRangeException(nameof(format), "Only PCM16 audio is currently supported.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        if (data.Length % (2L * channels) != 0)
            throw new ArgumentException("Audio data must contain complete PCM16 frames.", nameof(data));

        Data = data.ToArray();
        Format = format;
        SampleRate = sampleRate;
        Channels = channels;
    }
}
